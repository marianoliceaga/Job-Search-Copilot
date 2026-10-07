using Bunit;
using JobPilot.Domain.Pipeline;
using JobPilot.Domain.Tailoring;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Companies;
using JobPilot.Mobile.Core.Features.Outreach;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.Features.Tailoring;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Outreach;

/// <summary>Companies and outreach on the phone: cards instead of tables, drafts copied or shared, never sent.</summary>
public sealed class CompanyAndOutreachPagesTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeShareService _share = new();
    private readonly FakeLlmClient _llm = new();
    private Guid _postingId;

    public CompanyAndOutreachPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
        Services.AddSingleton<IFilePickerService>(new FakeFilePicker());
        Services.AddSingleton<IShareService>(_share);
        Services.AddSingleton<ILlmClient>(_llm);
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();
        var (master, _, posting) = ExportTests.Sample();
        await Services.GetRequiredService<MasterCvStore>().SaveAsync(master, "test", Ct);
        await using var db = await Services.GetRequiredService<IDbContextFactory<MobileDbContext>>().CreateDbContextAsync(Ct);
        db.JobPostings.Add(posting);
        await db.SaveChangesAsync(Ct);
        _postingId = posting.Id;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public async Task Companies_are_listed_as_cards_and_pipeline_companies_are_suggested()
    {
        await Services.GetRequiredService<ApplicationStore>().StartAsync(_postingId, ApplicationStage.Applied, Ct);
        await Services.GetRequiredService<CompanyStore>().CreateAsync("Northwind Traders", "https://northwind.example.com", Ct);

        var cut = Render<CompaniesPage>();

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=company-card]")));
        Assert.Contains("Northwind Traders", cut.Find("[data-testid=company-card]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Contoso Cloud", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public async Task Company_sources_open_outside_the_app()
    {
        var store = Services.GetRequiredService<CompanyStore>();
        var id = await store.CreateAsync("Northwind Traders", "https://northwind.example.com", Ct);

        var cut = Render<CompanyPage>(p => p.Add(c => c.Id, id));

        var source = cut.WaitForElement("[data-testid=company-source] a");
        Assert.Equal(("https://northwind.example.com", "_blank"), (source.GetAttribute("href"), source.GetAttribute("target")));
        Assert.Equal("_blank", cut.Find("a[href^='https://www.google.com/search']").GetAttribute("target"));
    }

    [Fact]
    public async Task Outreach_drafts_are_copied_or_shared_and_marked_as_sent_by_the_owner()
    {
        var applicationId = await Services.GetRequiredService<ApplicationStore>().StartAsync(_postingId, ApplicationStage.Applied, Ct);
        _llm.Returns(new OutreachResponse(
            [
                Variant(OutreachTone.Formal, "I am interested in the Senior .NET Engineer role and would value a short conversation."),
                Variant(OutreachTone.Friendly, "I came across the Senior .NET Engineer role and would love to chat about it."),
                Variant(OutreachTone.Direct, "I would like to apply for the Senior .NET Engineer role. Could we talk this week?"),
            ],
            "Tres tonos para el mismo pedido."));
        // The offer page links to /outreach?oferta={id}: the page reads the offer from the query string.
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/outreach?oferta={_postingId}");
        var cut = Render<OutreachPage>();

        cut.WaitForElements("button").Single(b => b.TextContent.Trim() == "Preparar borradores").Click();
        cut.WaitForElements("button").Single(b => b.TextContent.Trim() == "Generar con Gemini").Click();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("[data-testid=outreach-variant]").Count));
        cut.FindAll("[data-testid=share-outreach]")[0].Click();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Copiar").Click();

        var (title, subject, text) = Assert.Single(_share.SharedTexts);
        Assert.Equal(("Mensaje de LinkedIn", null), (title, subject));
        Assert.StartsWith("Hello,", text, StringComparison.Ordinal);
        Assert.Equal(text, Assert.Single(_share.Copied));

        cut.FindAll("button").First(b => b.TextContent.Trim() == "Ya lo envié").Click();
        cut.WaitForAssertion(() => Assert.Contains("Enviado el", cut.Find("[data-testid=outreach-history]").TextContent, StringComparison.Ordinal));
        var timeline = (await Services.GetRequiredService<ApplicationStore>().GetAsync(applicationId, Ct))!.Application.Events;
        Assert.Contains(timeline, e => e.Kind == ApplicationEventKind.MessageSent);
    }

    [Fact]
    public void Linkedin_proposals_are_copied_to_the_clipboard()
    {
        _llm.Returns(new LinkedInReviewResponse(
            "El titular es genérico.",
            ["Mencioná pagos."],
            [new TracedTextResponse("Tech Lead at Northwind Bank", ["E1"])],
            [new TracedTextResponse("Tech Lead at Northwind Bank since 2019.", ["E1"])]));
        var cut = Render<LinkedInReviewPanel>();

        cut.WaitForElements("button").Single(b => b.TextContent.Trim() == "Preparar revisión").Click();
        cut.WaitForElements("button").Single(b => b.TextContent.Trim() == "Revisar con Gemini").Click();
        cut.WaitForElement("[data-testid=linkedin-review]");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Copiar \"Acerca de\"").Click();

        Assert.Equal("Tech Lead at Northwind Bank since 2019.", Assert.Single(_share.Copied));
        Assert.Contains("Copiado al portapapeles", cut.Markup, StringComparison.Ordinal);
    }

    private static OutreachVariantResponse Variant(OutreachTone tone, string text) =>
        new(tone, string.Empty, [new OutreachParagraphResponse(text, [])]);
}
