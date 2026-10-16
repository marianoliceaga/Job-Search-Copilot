using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core.Features.Mail;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Tests.Features.Pipeline;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace JobPilot.Mobile.Tests.Google;

/// <summary>Scripted Google endpoints keyed by "METHOD path-prefix"; records every request (port of the web helper).</summary>
public sealed class FakeGoogleHandler : HttpMessageHandler
{
    private readonly List<(string Key, Func<HttpRequestMessage, string, HttpResponseMessage> Respond)> _routes = [];

    public List<(HttpMethod Method, string Url, string Body, string? Authorization)> Requests { get; } = [];

    public FakeGoogleHandler On(string method, string pathPrefix, HttpStatusCode status, object? body = null) =>
        On(method, pathPrefix, (_, _) => Json(status, body));

    public FakeGoogleHandler On(string method, string pathPrefix, Func<HttpRequestMessage, string, HttpResponseMessage> respond)
    {
        _routes.Insert(0, ($"{method} {pathPrefix}", respond));
        return this;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, object? body) =>
        new(status) { Content = new StringContent(body is null ? "{}" : JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var url = request.RequestUri!.AbsoluteUri;
        Requests.Add((request.Method, url, body, request.Headers.Authorization?.ToString()));
        var path = request.RequestUri.AbsolutePath;
        var route = _routes.FirstOrDefault(r => $"{request.Method} {path}".StartsWith(r.Key, StringComparison.Ordinal));
        return route.Respond is null ? Json(HttpStatusCode.NotFound, new { error = "not scripted" }) : route.Respond(request, body);
    }
}

/// <summary>The phone's Google services with Android's authorization API replaced by <see cref="FakeGoogleAuthorizer"/>.</summary>
public sealed class GoogleTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly PipelineTestContext _pipeline;
    private readonly TempDirectory _data;

    private GoogleTestHost(ServiceProvider services, PipelineTestContext pipeline, TempDirectory data, FakeGoogleHandler handler, FakeGoogleAuthorizer authorizer, FakeTimeProvider time)
    {
        _services = services;
        _pipeline = pipeline;
        _data = data;
        Handler = handler;
        Authorizer = authorizer;
        Time = time;
    }

    public FakeGoogleHandler Handler { get; }

    public FakeGoogleAuthorizer Authorizer { get; }

    public FakeTimeProvider Time { get; }

    public PipelineTestContext Pipeline => _pipeline;

    public string DataDirectory => _data.Path;

    public GoogleAuthService Auth => _services.GetRequiredService<GoogleAuthService>();

    public GmailClient Gmail => _services.GetRequiredService<GmailClient>();

    public GoogleCalendarClient Calendar => _services.GetRequiredService<GoogleCalendarClient>();

    public ApplicationGoogleService Applications => _services.GetRequiredService<ApplicationGoogleService>();

    public MailCheckService MailCheck => _services.GetRequiredService<MailCheckService>();

    public MailRulesStore MailRules => _services.GetRequiredService<MailRulesStore>();

    public static async Task<GoogleTestHost> CreateAsync()
    {
        var pipeline = await PipelineTestContext.CreateAsync();
        var data = new TempDirectory();
        var handler = new FakeGoogleHandler();
        var authorizer = new FakeGoogleAuthorizer();
        var time = pipeline.Jobs.Time;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Google:RevocationEndpoint"] = "https://oauth2.google.test/revoke",
            ["Google:GmailBaseUrl"] = "https://gmail.google.test/gmail/v1/",
            ["Google:CalendarBaseUrl"] = "https://calendar.google.test/calendar/v3/",
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton(new MobilePaths(data.Path))
            .AddSingleton<TimeProvider>(time)
            .AddSingleton<IGoogleAuthorizer>(authorizer)
            .AddSingleton(pipeline.Jobs.Documents)
            .AddSingleton(pipeline.Jobs.Db)
            .AddSingleton(pipeline.Store)
            .AddTransient<ApplicationGoogleService>()
            .AddSingleton(pipeline.Jobs.Ingestion)
            .AddSingleton(pipeline.Jobs.Preferences)
            .AddMail()
            .AddGoogle(configuration);
        services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));
        return new GoogleTestHost(services.BuildServiceProvider(), pipeline, data, handler, authorizer, time);
    }

    /// <summary>Simulates a completed consent with the given scopes; Android hands out "access-1".</summary>
    public async Task ConnectAsync(params string[] scopes)
    {
        var scopeList = scopes.Length == 0 ? GoogleScopes.All : scopes;
        Authorizer.Returns(new GoogleAuthorization("access-1", scopeList, false, null));
        var result = await Auth.ConnectAsync(GoogleAccess.Full, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.Error);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        await _pipeline.DisposeAsync();
        _data.Dispose();
    }
}

/// <summary>Android-specific authorization: Google Play services' AuthorizationClient, no tokens stored on the phone.</summary>
public class GoogleAuthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connecting_asks_android_for_the_minimal_scopes_and_stores_no_token()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        host.Authorizer.Returns(new GoogleAuthorization("secret-access", [GoogleScopes.CalendarAppCreated], false, null));

        var result = await host.Auth.ConnectAsync(GoogleAccess.Full, Ct);

        Assert.True(result.IsSuccess);
        var (scopes, interactive) = Assert.Single(host.Authorizer.Requests);
        Assert.Equal(GoogleScopes.All, scopes);
        Assert.True(interactive);
        var status = host.Auth.Status();
        Assert.True(status.CanUseCalendar);
        Assert.False(status.CanReadMail);
        Assert.Equal([GoogleScopes.GmailReadonly, GoogleScopes.GmailCompose], status.MissingScopes);
        var files = Directory.GetFiles(host.DataDirectory, "*", SearchOption.AllDirectories);
        Assert.All(files, f => Assert.DoesNotContain("secret-access", File.ReadAllText(f), StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_calendar_only_option_asks_only_for_the_jobpilot_calendar()
    {
        await using var host = await GoogleTestHost.CreateAsync();

        await host.Auth.ConnectAsync(GoogleAccess.CalendarOnly, Ct);

        Assert.Equal([GoogleScopes.CalendarAppCreated], host.Authorizer.Requests.Single().Scopes);
    }

    [Fact]
    public async Task A_cancelled_consent_or_a_phone_without_play_services_is_reported()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        host.Authorizer.Returns(GoogleAuthorization.Failed("Cancelaste el permiso en Google."));

        Assert.Equal("Cancelaste el permiso en Google.", (await host.Auth.ConnectAsync(GoogleAccess.Full, Ct)).Error);
        Assert.False(host.Auth.Status().Connected);

        host.Authorizer.IsAvailable = false;
        Assert.Contains("Google Play services", (await host.Auth.ConnectAsync(GoogleAccess.Full, Ct)).Error, StringComparison.Ordinal);
        Assert.False(host.Auth.Status().Configured);
    }

    [Fact]
    public async Task Later_sessions_ask_android_silently_and_report_a_revoked_grant()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();

        // The token from the consent is reused while it is fresh.
        Assert.Equal("access-1", (await host.Auth.GetAccessTokenAsync(Ct)).Value);
        Assert.Single(host.Authorizer.Requests);

        host.Time.Advance(TimeSpan.FromHours(1));
        host.Authorizer.Returns(new GoogleAuthorization("access-2", GoogleScopes.All, false, null));
        Assert.Equal("access-2", (await host.Auth.GetAccessTokenAsync(Ct)).Value);
        Assert.Equal(GoogleScopes.All, host.Authorizer.Requests[^1].Scopes);
        Assert.False(host.Authorizer.Requests[^1].Interactive);

        host.Time.Advance(TimeSpan.FromHours(1));
        host.Authorizer.Returns(GoogleAuthorization.Failed("Google pide volver a dar el permiso.", needsConsent: true));
        var failed = await host.Auth.GetAccessTokenAsync(Ct);
        Assert.Contains("Volvé a conectar", failed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_rejected_by_google_is_cleared_and_the_call_retried_once()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Authorizer.Returns(new GoogleAuthorization("access-2", GoogleScopes.All, false, null));
        host.Handler.On("GET", "/gmail/v1/users/me/profile", (request, _) => FakeGoogleHandler.Json(
            request.Headers.Authorization?.Parameter == "access-2" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, new { emailAddress = "alex@example.com" }));

        var email = await host.Gmail.ProfileEmailAsync(Ct);

        Assert.Equal("alex@example.com", email.Value);
        Assert.Equal(["access-1"], host.Authorizer.Cleared);
        Assert.Equal(["Bearer access-1", "Bearer access-2"], host.Handler.Requests.Select(r => r.Authorization));
    }

    [Fact]
    public async Task Disconnecting_revokes_the_grant_and_forgets_the_connection()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("POST", "/revoke", HttpStatusCode.OK);

        Assert.Null(await host.Auth.DisconnectAsync(Ct));

        Assert.Contains("token=access-1", host.Handler.Requests[^1].Body, StringComparison.Ordinal);
        Assert.Contains("access-1", host.Authorizer.Cleared);
        Assert.False(host.Auth.Status().Connected);
    }

    [Fact]
    public async Task A_scope_left_out_in_the_consent_blocks_the_call_without_reaching_google()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync(GoogleScopes.CalendarAppCreated);
        var before = host.Handler.Requests.Count;

        var result = await host.Gmail.SearchAsync("Contoso", 5, Ct);

        Assert.Contains("No diste ese permiso", result.Error, StringComparison.Ordinal);
        Assert.Equal(before, host.Handler.Requests.Count);
    }
}

public class GmailTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string B64(string text) => GmailClient.ToBase64Url(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Search_returns_thread_summaries_from_metadata()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("GET", "/gmail/v1/users/me/threads", HttpStatusCode.OK, new { threads = new[] { new { id = "t1", snippet = "Thanks" } } });
        host.Handler.On("GET", "/gmail/v1/users/me/threads/t1", HttpStatusCode.OK, new
        {
            id = "t1",
            messages = new[]
            {
                new { id = "m1", snippet = "Hi", internalDate = "1790000000000", payload = new { headers = new[] { new { name = "Subject", value = "Your application" }, new { name = "From", value = "Jordan <jordan@example.com>" } } } },
                new { id = "m2", snippet = "Thanks &amp; regards", internalDate = "1790003600000", payload = new { headers = new[] { new { name = "From", value = "Recruiting <jobs@example.com>" } } } },
            },
        });

        var result = await host.Gmail.SearchAsync("\"Contoso\"", 10, Ct);

        var thread = Assert.Single(result.Value!);
        Assert.Equal(("Your application", "Recruiting <jobs@example.com>", 2, "Thanks & regards"), (thread.Subject, thread.From, thread.Messages, thread.Snippet));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1790003600000).UtcDateTime, thread.DateUtc);
        Assert.Contains("q=%22Contoso%22", host.Handler.Requests.Single(r => r.Url.Contains("/threads?", StringComparison.Ordinal)).Url, StringComparison.Ordinal);
        Assert.All(host.Handler.Requests.Where(r => r.Url.Contains("gmail", StringComparison.Ordinal)), r => Assert.Equal("Bearer access-1", r.Authorization));
    }

    [Fact]
    public async Task Threads_are_read_as_plain_text_with_html_converted()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("GET", "/gmail/v1/users/me/threads/t2", HttpStatusCode.OK, new
        {
            id = "t2",
            messages = new object[]
            {
                new { id = "m1", payload = new { mimeType = "multipart/alternative", headers = new[] { new { name = "Subject", value = "Interview" }, new { name = "Message-ID", value = "<abc@example.com>" } },
                    parts = new[] { new { mimeType = "text/plain", body = new { data = B64("Can you talk on Monday? ñ") } }, new { mimeType = "text/html", body = new { data = B64("<b>ignored</b>") } } } } },
                new { id = "m2", payload = new { mimeType = "text/html", headers = Array.Empty<object>(), body = new { data = B64("<p>Hello <script>alert(1)</script>there</p>") } } },
            },
        });

        var thread = (await host.Gmail.GetThreadAsync("t2", Ct)).Value!;

        Assert.Equal("Interview", thread.Subject);
        Assert.Equal("Can you talk on Monday? ñ", thread.Messages[0].Body);
        Assert.Equal("<abc@example.com>", thread.Messages[0].MessageIdHeader);
        Assert.DoesNotContain("<", thread.Messages[1].Body, StringComparison.Ordinal);
        Assert.Contains("Hello", thread.Messages[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drafts_are_created_as_replies_with_safe_headers_and_never_sent()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("POST", "/gmail/v1/users/me/drafts", HttpStatusCode.OK, new { id = "d1", message = new { id = "m9" } });

        var result = await host.Gmail.CreateDraftAsync(new DraftRequest("jordan@example.com\r\nBcc: evil@example.com", "Re: Entrevista — señor", "Hello,\nThanks!", "t2", "<abc@example.com>"), Ct);

        Assert.Equal("d1", result.Value);
        var request = host.Handler.Requests.Single(r => r.Url.EndsWith("/drafts", StringComparison.Ordinal));
        var json = JsonNode.Parse(request.Body)!;
        Assert.Equal("t2", (string?)json["message"]!["threadId"]);
        var mime = Encoding.UTF8.GetString(GmailClient.FromBase64Url((string)json["message"]!["raw"]!));
        Assert.Contains("To: jordan@example.com  Bcc: evil@example.com\r\n", mime, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nBcc:", mime, StringComparison.Ordinal);
        Assert.Contains("Subject: =?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("Re: Entrevista — señor")) + "?=", mime, StringComparison.Ordinal);
        Assert.Contains("In-Reply-To: <abc@example.com>\r\nReferences: <abc@example.com>", mime, StringComparison.Ordinal);
        var body = mime[(mime.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4)..].Replace("\r\n", string.Empty, StringComparison.Ordinal);
        Assert.Equal("Hello,\r\nThanks!", Encoding.UTF8.GetString(Convert.FromBase64String(body)));
        Assert.DoesNotContain(host.Handler.Requests, r => r.Url.Contains("/send", StringComparison.Ordinal));
    }
}

public class GoogleCalendarTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_jobpilot_calendar_is_created_once_and_recreated_if_deleted()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("POST", "/calendar/v3/calendars", HttpStatusCode.OK, new { id = "cal-1" });
        host.Handler.On("POST", "/calendar/v3/calendars/cal-1/events", HttpStatusCode.OK, new { id = "ev-1" });
        host.Handler.On("GET", "/calendar/v3/calendars/cal-1", HttpStatusCode.OK, new { id = "cal-1" });
        var start = new DateTime(2026, 10, 10, 13, 0, 0, DateTimeKind.Utc);

        var first = await host.Calendar.CreateEventAsync(new CalendarEventRequest("Entrevista", "Video", start, start.AddHours(1)), Ct);
        var second = await host.Calendar.CreateEventAsync(new CalendarEventRequest("Entrevista 2", null, start, start.AddHours(1)), Ct);

        Assert.Equal(("ev-1", "ev-1"), (first.Value, second.Value));
        var calendars = host.Handler.Requests.Where(r => r.Method == HttpMethod.Post && r.Url.EndsWith("/calendars", StringComparison.Ordinal)).ToList();
        var created = JsonNode.Parse(Assert.Single(calendars).Body)!;
        Assert.Equal("JobSearch Copilot", (string?)created["summary"]);
        var eventBody = JsonNode.Parse(host.Handler.Requests.First(r => r.Url.EndsWith("/events", StringComparison.Ordinal)).Body)!;
        Assert.Equal("2026-10-10T13:00:00Z", (string?)eventBody["start"]!["dateTime"]);
        Assert.Equal("2026-10-10T14:00:00Z", (string?)eventBody["end"]!["dateTime"]);

        host.Handler.On("GET", "/calendar/v3/calendars/cal-1", HttpStatusCode.NotFound);
        host.Handler.On("POST", "/calendar/v3/calendars", HttpStatusCode.OK, new { id = "cal-2" });
        host.Handler.On("POST", "/calendar/v3/calendars/cal-2/events", HttpStatusCode.OK, new { id = "ev-2" });
        Assert.Equal("ev-2", (await host.Calendar.CreateEventAsync(new CalendarEventRequest("x", null, start, start.AddHours(1)), Ct)).Value);

        host.Handler.On("DELETE", "/calendar/v3/calendars/cal-2/events/gone", HttpStatusCode.NotFound);
        Assert.True((await host.Calendar.DeleteEventAsync("gone", Ct)).IsSuccess);
    }

    [Fact]
    public async Task Interviews_are_added_to_the_calendar_on_request_and_linked_by_id()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("POST", "/calendar/v3/calendars", HttpStatusCode.OK, new { id = "cal-1" });
        host.Handler.On("POST", "/calendar/v3/calendars/cal-1/events", HttpStatusCode.OK, new { id = "ev-9" });
        host.Handler.On("DELETE", "/calendar/v3/calendars/cal-1/events/ev-9", HttpStatusCode.NoContent);
        var store = host.Pipeline.Store;
        var id = await store.StartAsync(await host.Pipeline.AddPostingAsync(), ApplicationStage.Interview, Ct);
        var start = host.Pipeline.Now.AddDays(3);
        await store.ScheduleInterviewAsync(id, start, 45, "Technical with Jordan", Ct);
        var detail = (await store.GetAsync(id, Ct))!;
        var interview = detail.Application.Events.Single(e => e.Kind == ApplicationEventKind.InterviewScheduled);

        var preview = ApplicationGoogleService.EventFor(detail.Posting, interview);
        Assert.Null(await host.Applications.AddToCalendarAsync(detail, interview.Id, Ct));

        Assert.Equal("Entrevista: Senior .NET Engineer — Northwind", preview.Summary);
        Assert.Equal((start, start.AddMinutes(45)), (preview.StartUtc, preview.EndUtc));
        Assert.StartsWith("Technical with Jordan\nhttps://remotive.com/jobs/1", preview.Description, StringComparison.Ordinal);
        interview = (await store.GetAsync(id, Ct))!.Application.Events.Single(e => e.Kind == ApplicationEventKind.InterviewScheduled);
        Assert.Equal("ev-9", interview.ExternalId);

        Assert.Null(await host.Applications.RemoveFromCalendarAsync(id, interview, Ct));
        Assert.Null((await store.GetAsync(id, Ct))!.Application.Events.Single(e => e.Kind == ApplicationEventKind.InterviewScheduled).ExternalId);
    }
}

public class ApplicationMailTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Threads_are_linked_once_and_responses_recorded_without_the_body()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        var store = host.Pipeline.Store;
        var id = await store.StartAsync(await host.Pipeline.AddPostingAsync(), ApplicationStage.Applied, Ct);
        await store.AddContactAsync(id, new ApplicationContact { Name = "Jordan Lee", Email = "jordan@example.org" }, Ct);
        var detail = (await store.GetAsync(id, Ct))!;

        Assert.Equal("\"Northwind\" OR from:jordan@example.org OR to:jordan@example.org", ApplicationGoogleService.SuggestedQuery(detail));

        var summary = new MailThreadSummary("t1", "Your application", "Jordan", null, "", 1);
        await host.Applications.LinkAsync(id, summary, Ct);
        await host.Applications.LinkAsync(id, summary, Ct);
        var link = Assert.Single(await host.Applications.LinkedThreadsAsync(id, Ct));
        Assert.Equal("Your application", link.Subject);

        await host.Applications.RecordResponseAsync(id, new MailMessage("m1", "Jordan <jordan@example.org>", "me", "Next steps", null, "", "Private body text"), Ct);
        var response = (await store.GetAsync(id, Ct))!.Application.Events.Single(e => e.Kind == ApplicationEventKind.ResponseReceived);
        Assert.Equal("E-mail de Jordan <jordan@example.org>: Next steps", response.Text);

        host.Handler.On("GET", "/gmail/v1/users/me/threads/t1", HttpStatusCode.OK, new
        {
            id = "t1",
            messages = new object[]
            {
                new { id = "m1", payload = new { mimeType = "text/plain", headers = new[] { new { name = "Subject", value = "Your application" }, new { name = "From", value = "Jordan <jordan@example.org>" }, new { name = "Message-ID", value = "<j1@example.org>" } } } },
                new { id = "m2", payload = new { mimeType = "text/plain", headers = new[] { new { name = "From", value = "Alex <alex@example.com>" }, new { name = "Message-ID", value = "<a1@example.com>" } } } },
            },
        });
        await host.ConnectAsync();
        var reply = (await host.Applications.ReplyDraftAsync("t1", null, "Following up.", "alex@example.com", Ct)).Value!;
        Assert.Equal(("Jordan <jordan@example.org>", "Re: Your application", "<j1@example.org>", "t1"), (reply.To, reply.Subject, reply.InReplyTo, reply.ThreadId));

        await host.Applications.UnlinkAsync(id, link.Id, Ct);
        Assert.Empty(await host.Applications.LinkedThreadsAsync(id, Ct));
    }
}
