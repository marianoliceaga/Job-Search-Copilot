using System.Net;
using JobPilot.Domain.Mail;
using JobPilot.Domain.Pipeline;
using JobPilot.Web.Features.Mail;
using JobPilot.Web.Infrastructure.Google;
using JobPilot.Web.Infrastructure.Persistence;
using JobPilot.Web.Tests.Google;
using Microsoft.EntityFrameworkCore;

using static JobPilot.Web.Tests.Features.Mail.MailFixtures;

namespace JobPilot.Web.Tests.Features.Mail;

public class MailCheckServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void ScriptInbox(FakeGoogleHandler handler, DateTimeOffset now, string recruiterLastMessage = "m2")
    {
        handler.On("GET", "/gmail/v1/users/me/threads", HttpStatusCode.OK, new { threads = new[] { new { id = "t-alert" }, new { id = "t-recruiter" }, new { id = "t-news" }, new { id = "t-mine" } } });
        handler.On("GET", "/gmail/v1/users/me/threads/t-alert", HttpStatusCode.OK, new
        {
            id = "t-alert",
            messages = new[]
            {
                GmailMessage("m1", now.AddHours(-3), $"LinkedIn Job Alerts <{AlertSender}>", "New jobs for you",
                    text: AlertText("Senior .NET in Argentina"),
                    html: AlertHtml(
                        AlertCard("4000000001", "Senior .NET Engineer", "Contoso · Argentina (Remote)", "$4K-$5K / month"),
                        AlertCard("4000000002", "Java Developer", "Fabrikam · Austin, TX (Remote)")),
                    extraHeaders: [Header("X-LinkedIn-Template", "email_job_alert_digest_01"), Header("List-Unsubscribe", "<https://example.com/u>")]),
            },
        });
        handler.On("GET", "/gmail/v1/users/me/threads/t-recruiter", HttpStatusCode.OK, new
        {
            id = "t-recruiter",
            messages = new[] { GmailMessage(recruiterLastMessage, now.AddHours(-2), "Ana Recruiter <ana@example.com>", "Opportunity at Northwind", text: "We have a backend role for you.") },
        });
        handler.On("GET", "/gmail/v1/users/me/threads/t-news", HttpStatusCode.OK, new
        {
            id = "t-news",
            messages = new[] { GmailMessage("m3", now.AddHours(-1), "Weekly <news@example.org>", "Digest", text: "News", extraHeaders: [Header("List-Id", "<news.example.org>")]) },
        });
        handler.On("GET", "/gmail/v1/users/me/threads/t-mine", HttpStatusCode.OK, new
        {
            id = "t-mine",
            messages = new[]
            {
                GmailMessage("m4", now.AddHours(-5), "Ana Recruiter <ana@example.com>", "Interview", text: "Are you available?"),
                GmailMessage("m5", now.AddHours(-4), "Owner <owner@example.com>", "Re: Interview", text: "Yes", labels: ["SENT"]),
            },
        });
    }

    [Fact]
    public async Task A_check_sorts_the_mail_turns_alert_roles_into_offers_and_stores_only_facts()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        var now = host.Time.GetUtcNow();
        ScriptInbox(host.Handler, now);

        var outcome = await host.MailCheck.RunAsync(Ct);

        Assert.True(outcome.Succeeded, outcome.Error);
        var brief = (await host.MailCheck.GetBriefAsync(Ct))!;
        var check = brief.Check;
        Assert.Equal((4, 1, 1, 2, 2), (check.ThreadsRead, check.Waiting, check.AlertThreads, check.Filtered, check.Listings));
        Assert.Equal(1, check.OffersAdded);
        var role = Assert.Single(check.Roles);
        Assert.Equal(("Senior .NET Engineer", "Contoso", RoleFit.Strong, true), (role.Title, role.Company, role.Fit, role.IsNew));
        Assert.Equal(["+.NET en el título", "+senior", "+remoto LatAm/Argentina"], role.Why);
        Assert.Contains(check.RoleFilters, c => c.Label == "Remoto, solo EE. UU." && c.Count == 1);
        Assert.Contains(check.FilteredChips, c => c.Id == "you-wrote-last");
        Assert.Contains(check.FilteredChips, c => c.Id == "newsletters");

        var card = Assert.Single(brief.Waiting);
        Assert.Equal(("t-recruiter", "recruiter-outreach", "Ana Recruiter", "Respondele a Ana Recruiter: te interesa o no."),
            (card.ThreadId, card.RuleId, card.SenderName, card.NextAction));

        await using var db = await host.Pipeline.Jobs.Db.CreateDbContextAsync(Ct);
        var posting = await db.JobPostings.SingleAsync(p => p.Id == role.PostingId, Ct);
        Assert.Equal((MailCheckService.SourceKey, "https://www.linkedin.com/jobs/view/4000000001/", "Argentina (Remoto)", string.Empty),
            (posting.SourceKey, posting.Url, posting.Location, posting.Description));
        Assert.Equal((4000m, 5000m, "USD"), (posting.SalaryMin, posting.SalaryMax, posting.SalaryCurrency));

        // Read-only: only GET requests reach Gmail, and the first check reads the last day.
        Assert.All(host.Handler.Requests.Where(r => r.Url.Contains("gmail", StringComparison.Ordinal)), r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Contains(host.Handler.Requests, r => r.Url.Contains("q=in%3Ainbox%20newer_than%3A1d", StringComparison.Ordinal));
        Assert.DoesNotContain("backend role", System.Text.Json.JsonSerializer.Serialize(await db.MailWaiting.ToListAsync(Ct)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_next_check_reads_since_the_last_one_and_cards_stay_done_until_a_new_message()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        var first = host.Time.GetUtcNow();
        ScriptInbox(host.Handler, first);
        await host.MailCheck.RunAsync(Ct);
        var card = Assert.Single((await host.MailCheck.GetBriefAsync(Ct))!.Waiting);
        await host.MailCheck.MarkDoneAsync(card.Id, Ct);

        host.Time.Advance(TimeSpan.FromHours(3));
        await host.MailCheck.RunAsync(Ct);
        var second = (await host.MailCheck.GetBriefAsync(Ct))!;
        Assert.Empty(second.Waiting);
        Assert.Equal(0, second.Check.OffersAdded);
        Assert.Contains(host.Handler.Requests, r => r.Url.Contains($"after%3A{first.ToUnixTimeSeconds()}", StringComparison.Ordinal));

        ScriptInbox(host.Handler, host.Time.GetUtcNow(), recruiterLastMessage: "m9");
        await host.MailCheck.RunAsync(Ct);
        var third = (await host.MailCheck.GetBriefAsync(Ct))!;
        Assert.Equal("m9", Assert.Single(third.Waiting).LastMessageId);
    }

    [Fact]
    public async Task Without_mail_permission_or_with_broken_rules_nothing_is_read()
    {
        await using var host = await GoogleTestHost.CreateAsync();

        Assert.Contains("Conectá Google", (await host.MailCheck.RunAsync(Ct)).Error, StringComparison.Ordinal);

        await host.ConnectAsync(GoogleScopes.CalendarAppCreated);
        Assert.Contains("Gmail y Calendar", (await host.MailCheck.RunAsync(Ct)).Error, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Handler.Requests, r => r.Url.Contains("gmail", StringComparison.Ordinal));
        Assert.Null(await host.MailCheck.GetBriefAsync(Ct));
    }

    [Fact]
    public async Task Gmail_errors_are_recorded_as_a_failed_check_and_the_window_is_kept()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        await host.ConnectAsync();
        host.Handler.On("GET", "/gmail/v1/users/me/threads", HttpStatusCode.TooManyRequests, new { error = "rate" });

        var outcome = await host.MailCheck.RunAsync(Ct);

        Assert.False(outcome.Succeeded);
        var brief = (await host.MailCheck.GetBriefAsync(Ct))!;
        Assert.False(brief.Check.Succeeded);
        Assert.Null(brief.LastSuccessful);
        Assert.Contains("limitó", brief.Check.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_shipped_rules_compile_and_invalid_edits_are_rejected()
    {
        await using var host = await GoogleTestHost.CreateAsync();

        var json = await host.MailRules.GetJsonAsync(Ct);
        Assert.Null(MailRulesStore.Parse(json).Error);
        Assert.Contains("\"$missing\"", await host.MailRules.SaveJsonAsync(json.Replace("\"$hiringSystems\"", "\"$missing\"", StringComparison.Ordinal), Ct), StringComparison.Ordinal);
        Assert.StartsWith("El JSON no es válido", await host.MailRules.SaveJsonAsync("{ nope", Ct), StringComparison.Ordinal);
        Assert.Null(await host.MailRules.SaveJsonAsync(json.Replace("\"home\": []", "\"home\": [ \"cordoba\" ]", StringComparison.Ordinal), Ct));
        Assert.Equal(["cordoba"], (await host.MailRules.GetAsync(Ct)).Sets["home"]);
    }

    [Fact]
    public async Task Waiting_cards_suggest_the_application_of_the_company()
    {
        await using var host = await GoogleTestHost.CreateAsync();
        var posting = await host.Pipeline.AddPostingAsync("Backend Developer", "Northwind", "https://remotive.com/jobs/9");
        var applicationId = await host.Pipeline.Store.StartAsync(posting, ApplicationStage.Applied, Ct);
        var card = new MailWaitingRecord
        {
            ThreadId = "t1", LastMessageId = "m1", RuleId = "recruiter-outreach", Pill = "Responder", Subject = "Opportunity at Northwind",
            SenderName = "Ana", SenderAddress = "ana@example.com", Timeline = string.Empty, NextAction = string.Empty, RailNote = "Recibido",
        };

        var candidates = (await host.Pipeline.Store.GetBoardAsync(Ct)).Select(c => new MailApplicationCandidate(c.Id, c.Title, c.Company, [])).ToList();

        Assert.Equal(applicationId, Assert.Single(MailCheckService.SuggestApplications(card, candidates)).Id);
    }
}
