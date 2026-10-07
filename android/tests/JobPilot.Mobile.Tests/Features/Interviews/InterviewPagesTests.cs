using Bunit;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Negotiation;
using JobPilot.Domain.Pipeline;
using JobPilot.Domain.Practice;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Interviews;
using JobPilot.Mobile.Core.Features.Negotiation;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Features.Profile;
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

namespace JobPilot.Mobile.Tests.Features.Interviews;

/// <summary>Interviews and negotiation on the phone: section chips, cards instead of wide tables, and the practice chat.</summary>
public sealed class InterviewPagesTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeLlmClient _llm = new();
    private Guid _postingId;

    public InterviewPagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<IFilePickerService>(new FakeFilePicker());
        Services.AddSingleton<IShareService>(new FakeShareService());
        Services.AddSingleton<ILlmClient>(_llm);
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();
        var (master, _, posting) = ExportTests.Sample();
        await Services.GetRequiredService<MasterCvStore>().SaveAsync(master, "test", Ct);
        await using (var db = await Services.GetRequiredService<IDbContextFactory<MobileDbContext>>().CreateDbContextAsync(Ct))
        {
            db.JobPostings.Add(posting);
            await db.SaveChangesAsync(Ct);
        }

        _postingId = posting.Id;
        await Services.GetRequiredService<ApplicationStore>().StartAsync(_postingId, ApplicationStage.Interview, Ct);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public void The_interviews_page_shows_its_sections_as_chips()
    {
        var cut = Render<InterviewsPage>();

        cut.WaitForAssertion(() => Assert.Equal(
            ["Preparación", "Simulacros", "Todas las historias STAR"],
            cut.FindAll(".mud-chipset .mud-chip").Select(c => c.TextContent.Trim())));
        Assert.Empty(cut.FindAll(".mud-tab"));

        cut.FindAll(".mud-chipset .mud-chip").Single(c => c.TextContent.Trim() == "Simulacros").Click();
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=practice-history]")));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public async Task Offers_are_compared_in_cards()
    {
        var offers = Services.GetRequiredService<OfferStore>();
        Assert.Empty(await offers.SaveAsync(new CompensationOffer { Company = "Fabrikam", Currency = "USD", BaseAmount = 110000, Period = SalaryPeriod.Year }, Ct));

        var cut = Render<NegotiationPage>();

        var card = cut.WaitForElement("[data-testid=comparison-offer]");
        Assert.Contains("Fabrikam", card.TextContent, StringComparison.Ordinal);
        Assert.Contains("comparable por año", card.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("table"));
        Assert.Equal(["Comparador", "Guion", "Simulacro"], cut.FindAll(".mud-chipset .mud-chip").Select(c => c.TextContent.Trim()));
    }

    [Fact]
    public async Task The_practice_chat_takes_an_answer_and_shows_the_next_question()
    {
        _llm.Returns(new CoachTurnResponse(CoachMove.Question, 1, "Tell me about a performance problem you solved."))
            .Returns(new CoachTurnResponse(CoachMove.Question, 2, "Why do you want to join Contoso Cloud?"));
        var practice = Services.GetRequiredService<PracticeHandler>();
        var (prepared, error) = await practice.PrepareAsync(new PracticeSetup(PracticeMode.Interview, _postingId, null, InterviewFocus.Behavioral, 2, 10), Ct);
        Assert.Null(error);
        var (sessionId, startError) = await practice.StartAsync(prepared!, Ct);
        Assert.Null(startError);

        var cut = Render<PracticeSessionPage>(p => p.Add(c => c.Id, sessionId));

        cut.WaitForElement("[data-testid=coach-turn]");
        cut.Find("textarea").Input("At Northwind Bank I profiled the payment flow and fixed the slow queries.");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Responder").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[data-testid=coach-turn]").Count));
        Assert.Single(cut.FindAll("[data-testid=candidate-turn]"));
        Assert.Contains("Why do you want to join Contoso Cloud?", cut.Markup, StringComparison.Ordinal);
    }
}
