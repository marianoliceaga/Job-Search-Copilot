using System.Net;
using System.Text;
using System.Text.Json;
using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core.Features.Jobs;
using JobPilot.Mobile.Core.Features.Jobs.Sources;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Job feature services on a temporary database, with a controllable clock (port of the web helper).</summary>
public sealed class JobsTestContext : IAsyncDisposable
{
    private readonly TestDatabase _database;

    private JobsTestContext(TestDatabase database)
    {
        _database = database;
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        Documents = new SettingsDocumentStore(database.Factory, Time);
        Preferences = new JobPreferencesStore(Documents);
        Companies = new FollowedCompanyStore(database.Factory, Time);
        Ingestion = new JobIngestion(database.Factory, Time);
        Profiles = new ProfileStore(database.Factory, Time);
        Cvs = new MasterCvStore(database.Factory, Time);
        List = new JobListService(database.Factory, Preferences, Profiles);
    }

    public FakeTimeProvider Time { get; }

    public SettingsDocumentStore Documents { get; }

    public JobPreferencesStore Preferences { get; }

    public FollowedCompanyStore Companies { get; }

    public JobIngestion Ingestion { get; }

    public ProfileStore Profiles { get; }

    public MasterCvStore Cvs { get; }

    public JobListService List { get; }

    public Microsoft.EntityFrameworkCore.IDbContextFactory<MobileDbContext> Db => _database.Factory;

    /// <param name="keywords">Saves preferences with the tests' usual keywords (the app starts without any).</param>
    public static async Task<JobsTestContext> CreateAsync(bool keywords = true)
    {
        var context = new JobsTestContext(await TestDatabase.CreateAsync());
        if (keywords)
        {
            await context.Preferences.SaveAsync(new JobSearchPreferences { Keywords = [".net", "c#", "asp.net"], MaxAgeDays = 30 }, CancellationToken.None);
        }

        return context;
    }

    public static JobSourceOptions SourceOptions(int minIntervalMinutes = 60)
    {
        var options = new JobSourceOptions();
        foreach (var key in new[] { "remotive", "remoteok", "himalayas", "jobicy", "weworkremotely", "fake-a", "fake-b" })
        {
            options.Sources[key] = new JobSourceSettings { Urls = [$"https://{key}.test/feed"], MinIntervalMinutes = minIntervalMinutes };
        }

        options.Sources["greenhouse"] = new JobSourceSettings { Urls = ["https://gh.test/boards/{board}/jobs"], MinIntervalMinutes = minIntervalMinutes };
        options.Sources["lever"] = new JobSourceSettings { Urls = ["https://lever.test/postings/{board}"], MinIntervalMinutes = minIntervalMinutes };
        options.Sources["ashby"] = new JobSourceSettings { Urls = ["https://ashby.test/job-board/{board}"], MinIntervalMinutes = minIntervalMinutes };
        return options;
    }

    public JobSearchService Search(IEnumerable<IJobSource> sources, int minIntervalMinutes = 60) =>
        new(sources, Options.Create(SourceOptions(minIntervalMinutes)), Preferences, Companies, Ingestion, Db, Time, NullLogger<JobSearchService>.Instance);

    /// <summary>A fictional profile (the app starts with an empty one).</summary>
    public async Task SaveProfileAsync()
    {
        var profile = await Profiles.GetAsync(CancellationToken.None);
        profile.Headline = "Senior .NET Engineer";
        profile.WorkFromCountry = "Argentina";
        profile.ApplicationEmphases = ["Recent hands-on experience with Blazor"];
        await Profiles.SaveAsync(profile, CancellationToken.None);
    }

    public async Task SaveCvAsync()
    {
        var cv = new MasterCv
        {
            Headline = "Senior .NET Engineer",
            Experiences = [new CvExperience { Company = "Northwind", Title = "Tech Lead", Start = new PartialDate(2019), IsCurrent = true, Technologies = ["C#", "Azure", "Blazor"] }],
            Skills = [new CvSkill { Name = "C#" }, new CvSkill { Name = "ASP.NET Core" }],
        };
        await Cvs.SaveAsync(cv, "test", CancellationToken.None);
    }

    public static SourcedJob Job(string title = "Senior .NET Engineer", string company = "Northwind", string url = "https://remotive.com/jobs/1",
        string source = "remotive", string description = "C# and ASP.NET Core", DateTime? published = null) => new()
    {
        SourceKey = source,
        SourceName = source,
        Title = title,
        Company = company,
        Url = url,
        Description = description,
        PublishedAtUtc = published ?? new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
    };

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
    }
}

/// <summary>Returns canned responses by URL prefix.</summary>
public sealed class StubHttpHandler(Dictionary<string, (HttpStatusCode Status, string Body)> responses) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);
        var match = responses.FirstOrDefault(r => url.StartsWith(r.Key, StringComparison.Ordinal));
        return Task.FromResult(match.Key is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(match.Value.Status) { Content = new StringContent(match.Value.Body, Encoding.UTF8, "application/json") });
    }
}

/// <summary>A scripted job source.</summary>
public sealed class FakeJobSource(string key, Func<SourceFetchContext, CancellationToken, Task<SourceFetchResult>> fetch) : IJobSource
{
    public int Calls { get; private set; }

    public string Key => key;

    public string DisplayName => key;

    public string TermsSummary => "test";

    public Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        Calls++;
        return fetch(context, cancellationToken);
    }

    public static FakeJobSource Returning(string key, params SourcedJob[] jobs) =>
        new(key, (_, _) => Task.FromResult(new SourceFetchResult(jobs, [])));
}
