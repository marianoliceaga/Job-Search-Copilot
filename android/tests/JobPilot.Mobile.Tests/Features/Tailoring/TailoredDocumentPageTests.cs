using Bunit;
using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using UglyToad.PdfPig;

namespace JobPilot.Mobile.Tests.Features.Tailoring;

/// <summary>Approved documents leave the phone only through Android's share sheet, and only after approval.</summary>
public sealed class TailoredDocumentPageTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeShareService _share = new();
    private Guid _postingId;
    private long _documentId;

    public TailoredDocumentPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Notifications.INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
        Services.AddSingleton<IFilePickerService>(new FakeFilePicker());
        Services.AddSingleton<IShareService>(_share);
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();
        var (master, tailored, posting) = ExportTests.Sample();
        var version = (await Services.GetRequiredService<MasterCvStore>().SaveAsync(master, "test", Ct)).Version!.Value;
        await using (var db = await Services.GetRequiredService<IDbContextFactory<MobileDbContext>>().CreateDbContextAsync(Ct))
        {
            db.JobPostings.Add(posting);
            await db.SaveChangesAsync(Ct);
        }

        _postingId = posting.Id;
        _documentId = await Services.GetRequiredService<TailoredDocumentStore>()
            .AddDraftAsync(posting.Id, TailoredKind.Cv, version, tailored, "cv-tailoring.v1", "gemini-test", null, Ct);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public void Drafts_cannot_be_shared()
    {
        var cut = Render<TailoredDocumentPage>(p => p.Add(c => c.PostingId, _postingId).Add(c => c.Id, _documentId));

        cut.WaitForAssertion(() => Assert.Contains("se habilita al aprobar", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("[data-testid=share-pdf]"));
    }

    [Fact]
    public async Task An_approved_cv_is_shared_as_pdf_and_docx_through_the_share_sheet()
    {
        await Services.GetRequiredService<TailoredDocumentStore>().SetStatusAsync(_documentId, TailoredStatus.Approved, Ct);
        var cut = Render<TailoredDocumentPage>(p => p.Add(c => c.PostingId, _postingId).Add(c => c.Id, _documentId));

        cut.WaitForElement("[data-testid=share-pdf]").Click();
        cut.Find("[data-testid=share-docx]").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _share.Shared.Count));
        var (_, pdfName, pdfType, pdf) = _share.Shared[0];
        Assert.Equal(("Alex Rivera - CV - Contoso Cloud.pdf", "application/pdf"), (pdfName, pdfType));
        using (var document = PdfDocument.Open(pdf))
        {
            Assert.Contains("Cut payment latency by 40%.", document.GetPage(1).Text, StringComparison.Ordinal);
        }

        Assert.EndsWith(".docx", _share.Shared[1].FileName, StringComparison.Ordinal);
    }
}
