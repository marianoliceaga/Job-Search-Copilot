using Bunit;
using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Jobs;
using JobPilot.Mobile.Core.Features.Jobs.Sources;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Jobs;

public sealed class JobsPageTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();

    public JobsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Notifications.INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
        Services.AddSingleton<IFilePickerService>(new FakeFilePicker());
    }

    public async ValueTask InitializeAsync() => await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public void The_sources_and_intervals_are_the_web_app_ones()
    {
        var options = Services.GetRequiredService<IOptions<JobSourceOptions>>().Value;
        var sources = Services.GetServices<IJobSource>().Select(s => s.Key).ToList();

        Assert.Equal(["remotive", "remoteok", "himalayas", "jobicy", "weworkremotely", "greenhouse", "lever", "ashby"], sources);
        Assert.Equal(360, options.For("remotive").MinIntervalMinutes);
        Assert.Contains("personal job search", options.UserAgent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_offers_page_has_the_web_tabs()
    {
        var cut = Render<JobsPage>();

        Assert.Equal(["Ofertas", "Fuentes y empresas", "Preferencias"], cut.FindAll(".mud-tab").Select(t => t.TextContent.Trim()));
    }

    [Fact]
    public async Task The_offer_page_links_to_the_source_outside_the_app_and_offers_company_outreach_and_pipeline()
    {
        var ingestion = Services.GetRequiredService<JobIngestion>();
        await ingestion.IngestAsync([new SourcedJob
        {
            SourceKey = "remotive", SourceName = "Remotive", Title = "Senior .NET Engineer", Company = "Northwind",
            Url = "https://remotive.com/remote-jobs/software-dev/senior-net-1", Description = "C# and ASP.NET Core. Remote, LATAM.",
            PublishedAtUtc = DateTime.UtcNow.AddDays(-1),
        }], new JobSearchPreferences(), applyFilters: false, Xunit.TestContext.Current.CancellationToken);
        var id = (await Services.GetRequiredService<JobListService>().GetAsync(new JobListFilter(), Xunit.TestContext.Current.CancellationToken)).Single().Posting.Id;

        var cut = Render<JobDetailPage>(p => p.Add(c => c.Id, id));

        var link = cut.WaitForElement("a[href='https://remotive.com/remote-jobs/software-dev/senior-net-1']");
        // target=_blank makes BlazorWebView open it in the phone's browser (external link), never inside the app.
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("Agregar al pipeline", cut.Markup, StringComparison.Ordinal);
        Assert.NotNull(cut.Find($"a[href='/outreach?oferta={id}']"));
        Assert.Contains("Crear ficha de la empresa", cut.Markup, StringComparison.Ordinal);
    }
}
