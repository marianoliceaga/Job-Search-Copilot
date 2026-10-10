using JobPilot.Domain.Negotiation;
using JobPilot.Domain.Pipeline;
using JobPilot.Mobile.Core.Features.Negotiation;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Features.Transfer;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using JobPilot.Mobile.Tests.Features.Pipeline;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Tests.Features.Transfer;

/// <summary>A device for transfer tests: its own database, clock, stores and backup folder.</summary>
internal sealed class TransferDevice : IAsyncDisposable
{
    private readonly TempDirectory _data = new();

    private TransferDevice(PipelineTestContext pipeline)
    {
        Pipeline = pipeline;
        Offers = new OfferStore(pipeline.Jobs.Db, pipeline.Jobs.Documents, pipeline.Jobs.Preferences, pipeline.Jobs.Time);
        Transfer = new TransferService(pipeline.Jobs.Db, new MobilePaths(_data.Path), pipeline.Jobs.Time, pipeline.Sync);
    }

    public PipelineTestContext Pipeline { get; }

    public ApplicationStore Store => Pipeline.Store;

    public OfferStore Offers { get; }

    public TransferService Transfer { get; }

    public static async Task<TransferDevice> CreateAsync() => new(await PipelineTestContext.CreateAsync());

    public async Task<TransferData> ReadAsync()
    {
        await using var db = await Pipeline.Jobs.Db.CreateDbContextAsync(TestContext.Current.CancellationToken);
        return await TransferDatabase.ReadAsync(db, TestContext.Current.CancellationToken);
    }

    /// <summary>Exports this device and combines the file into <paramref name="target"/>, as the owner would.</summary>
    public async Task<TransferImportResult?> SendToAsync(TransferDevice target)
    {
        var (_, content) = await Transfer.ExportAsync(TestContext.Current.CancellationToken);
        var (preview, error) = await target.Transfer.PreviewAsync(new MemoryStream(content), TestContext.Current.CancellationToken);
        Assert.Null(error);
        return preview!.HasChanges ? await target.Transfer.ImportAsync(preview.File, TestContext.Current.CancellationToken) : null;
    }

    public async ValueTask DisposeAsync()
    {
        await Pipeline.DisposeAsync();
        _data.Dispose();
    }
}

public class TransferTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_transfer_schema_lists_every_table_and_column_of_the_database()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var db = await database.Factory.CreateDbContextAsync(Ct);

        var model = db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null)
            .ToDictionary(e => e.GetTableName()!, e => e.GetProperties().Select(p => p.GetColumnName()).Order(StringComparer.Ordinal).ToList());

        Assert.Equal(model.Keys.Order(StringComparer.Ordinal), TransferSchema.Tables.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(TransferSchema.Tables, table => Assert.Equal(model[table.Name], table.Columns.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task Owner_deletions_are_recorded_with_the_identity_of_the_row()
    {
        await using var device = await TransferDevice.CreateAsync();
        var posting = await device.Pipeline.AddPostingAsync();
        var applicationId = await device.Store.StartAsync(posting, ApplicationStage.Applied, Ct);
        await device.Store.AddNoteAsync(applicationId, "Called the recruiter", Ct);
        await device.Store.AddContactAsync(applicationId, new ApplicationContact { Name = "Ana" }, Ct);
        Assert.Empty(await device.Offers.SaveAsync(new CompensationOffer { Company = "Contoso", Currency = "USD", BaseAmount = 5000 }, Ct));
        var before = await device.ReadAsync();
        var detail = (await device.Store.GetAsync(applicationId, Ct))!.Application;

        await device.Store.DeleteEventAsync(applicationId, detail.Events.Single(e => e.Kind == ApplicationEventKind.Note).Id, Ct);
        await device.Store.DeleteContactAsync(applicationId, detail.Contacts.Single().Id, Ct);
        await device.Offers.DeleteAsync((await device.Offers.ListAsync(Ct)).Single().Id, Ct);
        await device.Store.DeleteAsync(applicationId, Ct);

        await using var db = await device.Pipeline.Jobs.Db.CreateDbContextAsync(Ct);
        var records = await db.DeletedRecords.AsNoTracking().OrderBy(r => r.Id).ToListAsync(Ct);
        Assert.Equal([TransferSchema.ApplicationEvents, TransferSchema.ApplicationContacts, TransferSchema.CompensationOffers, TransferSchema.Applications],
            records.Select(r => r.TableName));
        Assert.All(records, record =>
        {
            // The recorded identity matches the raw row exactly (same text form for dates and GUIDs).
            var table = TransferSchema.Get(record.TableName);
            var identity = TransferValues.ReadIdentity(record.Identity)!;
            Assert.Contains(before.Rows(table.Name), row => TransferValues.IdentityKey(table, row) == TransferValues.IdentityKey(table, identity));
        });
    }

    [Fact]
    public async Task Two_devices_combine_their_data_deletions_included_and_a_second_import_changes_nothing()
    {
        await using var pc = await TransferDevice.CreateAsync();
        await using var phone = await TransferDevice.CreateAsync();
        var posting = await pc.Pipeline.AddPostingAsync();
        var applicationId = await pc.Store.StartAsync(posting, ApplicationStage.Applied, Ct);
        await pc.Store.AddNoteAsync(applicationId, "Sent the CV", Ct);
        await pc.Store.AddContactAsync(applicationId, new ApplicationContact { Name = "Ana", Email = "ana@example.com" }, Ct);

        var replacements = phone.Pipeline.Notifications.Replacements;
        var first = await pc.SendToAsync(phone);

        Assert.NotNull(first);
        Assert.True(File.Exists(first.BackupPath));
        Assert.True(phone.Pipeline.Notifications.Replacements > replacements, "The phone reschedules its reminders after combining.");
        var onPhone = Assert.Single(await phone.Store.GetBoardAsync(Ct));
        Assert.Equal((applicationId, ApplicationStage.Applied), (onPhone.Id, onPhone.Stage));
        var phoneDetail = (await phone.Store.GetAsync(onPhone.Id, Ct))!.Application;
        Assert.Equal("ana@example.com", Assert.Single(phoneDetail.Contacts).Email);

        // Later, on the phone: the note is deleted and the application moves to Interview.
        phone.Pipeline.Jobs.Time.Advance(TimeSpan.FromHours(2));
        await phone.Store.DeleteEventAsync(onPhone.Id, phoneDetail.Events.Single(e => e.Kind == ApplicationEventKind.Note).Id, Ct);
        await phone.Store.MoveAsync(onPhone.Id, ApplicationStage.Interview, null, Ct);

        Assert.NotNull(await phone.SendToAsync(pc));

        var pcDetail = (await pc.Store.GetAsync(applicationId, Ct))!.Application;
        Assert.Equal(ApplicationStage.Interview, pcDetail.Stage);
        Assert.DoesNotContain(pcDetail.Events, e => e.Kind == ApplicationEventKind.Note);
        Assert.Null(await phone.SendToAsync(pc));
        Assert.Null(await pc.SendToAsync(phone));
    }

    [Fact]
    public async Task A_file_exported_by_the_pc_is_combined_here()
    {
        await using var device = await TransferDevice.CreateAsync();
        var content = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Transfer", "pc-export.json"), Ct);

        var (preview, error) = await device.Transfer.PreviewAsync(new MemoryStream(content), Ct);
        Assert.Null(error);
        Assert.Equal("web", preview!.File.App);
        await device.Transfer.ImportAsync(preview.File, Ct);

        var card = Assert.Single(await device.Store.GetBoardAsync(Ct));
        Assert.Equal(("Backend Engineer (.NET)", "Fabrikam", ApplicationStage.Applied), (card.Title, card.Company, card.Stage));
        var detail = (await device.Store.GetAsync(card.Id, Ct))!.Application;
        Assert.Contains(detail.Events, e => e.Text == "Fictional note from the web app");
        Assert.Equal("jordan.lee@example.com", Assert.Single(detail.Contacts).Email);
        Assert.Equal("Fabrikam", Assert.Single(await device.Offers.ListAsync(Ct)).Company);
        var (again, _) = await device.Transfer.PreviewAsync(new MemoryStream(content), Ct);
        Assert.False(again!.HasChanges);
    }

    [Fact]
    public async Task A_file_from_another_version_is_refused_before_reading_the_database()
    {
        await using var device = await TransferDevice.CreateAsync();

        var (preview, error) = await device.Transfer.PreviewAsync(new MemoryStream("""{"format":"jobpilot-transfer","version":0}"""u8.ToArray()), Ct);

        Assert.Null(preview);
        Assert.Contains("otra versión", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_install_takes_the_profile_of_an_imported_file_even_if_the_file_is_older()
    {
        await using var owner = await TransferDevice.CreateAsync();
        await using var phone = await TransferDevice.CreateAsync();
        var profile = await owner.Pipeline.Jobs.Profiles.GetAsync(Ct);
        profile.Headline = "Backend Engineer";
        profile.Stack = ["C#"];
        await owner.Pipeline.Jobs.Profiles.SaveAsync(profile, Ct);
        phone.Pipeline.Jobs.Time.Advance(TimeSpan.FromDays(3));
        Assert.True(JobPilot.Mobile.Core.Features.Profile.ProfileStore.IsBlank(await phone.Pipeline.Jobs.Profiles.GetAsync(Ct)));

        await owner.SendToAsync(phone);

        var imported = await phone.Pipeline.Jobs.Profiles.GetAsync(Ct);
        Assert.Equal("Backend Engineer", imported.Headline);
        Assert.Equal(["C#"], imported.Stack);
    }
}
