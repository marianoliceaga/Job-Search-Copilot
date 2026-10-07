using JobPilot.Mobile.Core.Features.Transfer;

using static JobPilot.Mobile.Tests.Features.Transfer.TransferTestData;

namespace JobPilot.Mobile.Tests.Features.Transfer;

public class TransferMergerTests
{
    private const string AppOnPc = "AAAAAAAA-0000-0000-0000-000000000001";
    private const string AppOnPhone = "AAAAAAAA-0000-0000-0000-000000000002";

    [Fact]
    public void Into_an_empty_device_everything_is_added_with_new_ids_and_translated_references()
    {
        var file = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.Applications, Application(AppOnPc, PostingA, "Applied", At(2))),
            (TransferSchema.ApplicationEvents, Event(40, AppOnPc, "Created", At(1), toStage: "Interested")),
            (TransferSchema.ApplicationEvents, Event(41, AppOnPc, "StageChanged", At(2), toStage: "Applied")),
            (TransferSchema.JobAnalyses, Row(TransferSchema.JobAnalyses, ("Id", 9L), ("JobPostingId", PostingA), ("CreatedAtUtc", At(2)), ("Json", "{}"))));

        var plan = TransferMerger.Merge(TransferData.Empty(), file);

        Assert.Equal(1, plan.Changes(TransferSchema.JobPostings).Added);
        var events = plan.Changes(TransferSchema.ApplicationEvents).Changes.Inserts;
        Assert.Equal([1L, 2L], events.Select(e => e["Id"]));
        Assert.All(events, e => Assert.Equal(AppOnPc, e["ApplicationId"]));
        Assert.Equal(1L, Assert.Single(plan.Changes(TransferSchema.JobAnalyses).Changes.Inserts)["Id"]);
        Assert.Equal("Applied", Assert.Single(plan.Changes(TransferSchema.Applications).Changes.Inserts)["Stage"]);
    }

    [Fact]
    public void The_same_posting_found_on_both_devices_is_matched_by_url_and_children_follow_the_local_id()
    {
        var phone = Data((TransferSchema.JobPostings, Posting(PostingB, "https://example.com/jobs/1", description: "Pasted on the phone", lastSeen: At(5))));
        var pc = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1", description: "", lastSeen: At(3))),
            (TransferSchema.Applications, Application(AppOnPc, PostingA, "Interested", At(1))),
            (TransferSchema.ApplicationEvents, Event(1, AppOnPc, "Created", At(1), toStage: "Interested")));

        var plan = TransferMerger.Merge(phone, pc);

        Assert.Equal(0, plan.Changes(TransferSchema.JobPostings).Added);
        var application = Assert.Single(plan.Changes(TransferSchema.Applications).Changes.Inserts);
        Assert.Equal(PostingB, application["JobPostingId"]);
        var after = Apply(phone, plan);
        var posting = Assert.Single(after.Rows(TransferSchema.JobPostings));
        Assert.Equal(("Pasted on the phone", At(5)), (posting["Description"], posting["LastSeenAtUtc"]));
    }

    [Fact]
    public void Dated_rows_keep_the_latest_change_and_undated_edits_take_the_file()
    {
        var company = (string notes, string updated) => Row(TransferSchema.Companies, ("Id", "C0000000-0000-0000-0000-000000000001"), ("NameKey", "northwind"),
            ("Name", "Northwind"), ("Notes", notes), ("CreatedAtUtc", At(1)), ("UpdatedAtUtc", updated), ("Facts", "[]"), ("Gaps", "[]"), ("Sources", "[]"));
        var offer = (string company) => Row(TransferSchema.CompensationOffers, ("Id", 3L), ("CreatedAtUtc", At(2)), ("Company", company), ("Currency", "USD"));
        var outreach = (string? sentAt) => Row(TransferSchema.OutreachMessages, ("Id", 1L), ("CreatedAtUtc", At(2)), ("Channel", "Email"), ("SentAtUtc", sentAt), ("Json", "{}"));

        var device = Data((TransferSchema.Companies, company("newer notes", At(6))), (TransferSchema.CompensationOffers, offer("Old name")),
            (TransferSchema.OutreachMessages, outreach(At(4))));
        var file = Data((TransferSchema.Companies, company("older notes", At(5))), (TransferSchema.CompensationOffers, offer("Edited name")),
            (TransferSchema.OutreachMessages, outreach(null)));

        var after = Apply(device, TransferMerger.Merge(device, file));

        Assert.Equal("newer notes", Assert.Single(after.Rows(TransferSchema.Companies))["Notes"]);
        Assert.Equal("Edited name", Assert.Single(after.Rows(TransferSchema.CompensationOffers))["Company"]);
        Assert.Equal(At(4), Assert.Single(after.Rows(TransferSchema.OutreachMessages))["SentAtUtc"]);
    }

    [Fact]
    public void The_stage_and_reminder_follow_the_combined_timeline()
    {
        // The phone moved the application to Interview on day 3; the PC set a reminder on day 4 while it still showed Applied.
        var phone = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.Applications, Application(AppOnPhone, PostingA, "Interview", At(3))),
            (TransferSchema.ApplicationEvents, Event(1, AppOnPhone, "Created", At(1), toStage: "Applied")),
            (TransferSchema.ApplicationEvents, Event(2, AppOnPhone, "StageChanged", At(3), toStage: "Interview")));
        var pc = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.Applications, Row(TransferSchema.Applications, ("Id", AppOnPc), ("JobPostingId", PostingA), ("Stage", "Applied"),
                ("CreatedAtUtc", At(1)), ("StageChangedAtUtc", At(1)), ("ReminderAtUtc", At(9)))),
            (TransferSchema.ApplicationEvents, Event(7, AppOnPc, "Created", At(1), toStage: "Applied")),
            (TransferSchema.ApplicationEvents, Event(8, AppOnPc, "ReminderSet", At(4), scheduledAt: At(9))),
            (TransferSchema.ApplicationEvents, Event(9, AppOnPc, "Note", At(4, 12), text: "Called the recruiter")));

        var after = Apply(phone, TransferMerger.Merge(phone, pc));

        var application = Assert.Single(after.Rows(TransferSchema.Applications));
        Assert.Equal((AppOnPhone, "Interview", At(3), At(9)), (application["Id"], application["Stage"], application["StageChangedAtUtc"], application["ReminderAtUtc"]));
        Assert.Equal(["Created", "StageChanged", "ReminderSet", "Note"], after.Rows(TransferSchema.ApplicationEvents).Select(e => e["Kind"]));
        Assert.All(after.Rows(TransferSchema.ApplicationEvents), e => Assert.Equal(AppOnPhone, e["ApplicationId"]));
    }

    [Fact]
    public void Deletions_travel_both_ways_unless_the_row_changed_afterwards()
    {
        var note = Event(5, AppOnPhone, "Note", At(2), text: "Old note");
        var phone = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.Applications, Application(AppOnPhone, PostingA, "Applied", At(1))),
            (TransferSchema.ApplicationEvents, Event(1, AppOnPhone, "Created", At(1), toStage: "Applied")),
            (TransferSchema.ApplicationEvents, note),
            // The phone deleted an offer of the comparator that the PC still has.
            (TransferSchema.DeletedRecords, Deleted(1, TransferSchema.CompensationOffers, $$"""{"CreatedAtUtc":"{{At(2)}}"}""", At(6))));
        var pc = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.Applications, Application(AppOnPc, PostingA, "Applied", At(1))),
            (TransferSchema.ApplicationEvents, Event(1, AppOnPc, "Created", At(1), toStage: "Applied")),
            (TransferSchema.CompensationOffers, Row(TransferSchema.CompensationOffers, ("Id", 1L), ("CreatedAtUtc", At(2)), ("Company", "Contoso"))),
            (TransferSchema.CompensationOffers, Row(TransferSchema.CompensationOffers, ("Id", 2L), ("CreatedAtUtc", At(7)), ("Company", "Fabrikam"))),
            // The PC deleted the note (its identity uses the PC's application id).
            (TransferSchema.DeletedRecords, Deleted(1, TransferSchema.ApplicationEvents,
                $$"""{"ApplicationId":"{{AppOnPc}}","Kind":"Note","OccurredAtUtc":"{{At(2)}}"}""", At(5))));

        var plan = TransferMerger.Merge(phone, pc);
        var after = Apply(phone, plan);

        Assert.DoesNotContain(after.Rows(TransferSchema.ApplicationEvents), e => e["Kind"] as string == "Note");
        Assert.Equal(["Fabrikam"], after.Rows(TransferSchema.CompensationOffers).Select(o => o["Company"]));
        var recorded = Assert.Single(plan.Changes(TransferSchema.DeletedRecords).Changes.Inserts);
        Assert.Contains(AppOnPhone, (string)recorded["Identity"]!, StringComparison.Ordinal);

        // Importing the same file again changes nothing.
        Assert.False(TransferMerger.Merge(after, pc).HasChanges);
    }

    [Fact]
    public void An_application_added_again_after_a_deletion_is_kept()
    {
        var phone = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.Applications, Application(AppOnPhone, PostingA, "Interested", At(8))),
            (TransferSchema.ApplicationEvents, Event(1, AppOnPhone, "Created", At(8), toStage: "Interested")));
        var pc = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.DeletedRecords, Deleted(1, TransferSchema.Applications, $$"""{"JobPostingId":"{{PostingA}}"}""", At(5))));

        var plan = TransferMerger.Merge(phone, pc);

        Assert.Equal(0, plan.Changes(TransferSchema.Applications).Deleted);
        Assert.Single(plan.Changes(TransferSchema.DeletedRecords).Changes.Inserts);
    }

    [Fact]
    public void Master_cv_versions_that_collide_are_renumbered_and_references_follow()
    {
        var version = (long id, long number, string at) => Row(TransferSchema.MasterCvVersions, ("Id", id), ("Version", number), ("CreatedAtUtc", at),
            ("Json", "{}"), ("Source", "editor"));
        var phone = Data((TransferSchema.MasterCvVersions, version(1, 1, At(1))), (TransferSchema.MasterCvVersions, version(2, 2, At(3))),
            (TransferSchema.MasterCvs, Row(TransferSchema.MasterCvs, ("Id", 1L), ("Version", 2L), ("UpdatedAtUtc", At(3)), ("Json", "{\"phone\":true}"))));
        var pc = Data((TransferSchema.MasterCvVersions, version(1, 1, At(1))), (TransferSchema.MasterCvVersions, version(2, 2, At(4))),
            (TransferSchema.MasterCvs, Row(TransferSchema.MasterCvs, ("Id", 1L), ("Version", 2L), ("UpdatedAtUtc", At(4)), ("Json", "{\"pc\":true}"))),
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.TailoredDocuments, Row(TransferSchema.TailoredDocuments, ("Id", 1L), ("JobPostingId", PostingA), ("Kind", "Cv"),
                ("CreatedAtUtc", At(4)), ("UpdatedAtUtc", At(4)), ("MasterCvVersion", 2L), ("Json", "{}"))));

        var after = Apply(phone, TransferMerger.Merge(phone, pc));

        Assert.Equal([1L, 2L, 3L], after.Rows(TransferSchema.MasterCvVersions).Select(v => v["Version"]).Order());
        var cv = Assert.Single(after.Rows(TransferSchema.MasterCvs));
        Assert.Equal(("{\"pc\":true}", 3L), (cv["Json"], cv["Version"]));
        Assert.Equal(3L, Assert.Single(after.Rows(TransferSchema.TailoredDocuments))["MasterCvVersion"]);
    }

    [Fact]
    public void Only_newer_mail_checks_are_added_and_done_cards_stay_done()
    {
        var check = (long id, string at) => Row(TransferSchema.MailChecks, ("Id", id), ("StartedAtUtc", at), ("FinishedAtUtc", at), ("Succeeded", 1L),
            ("FilteredChips", "[]"), ("RoleFilters", "[]"), ("Warnings", "[]"), ("Roles", "[]"), ("WindowSinceUtc", At(1)));
        var card = (long id, long checkId, string? doneAt) => Row(TransferSchema.MailWaiting, ("Id", id), ("ThreadId", "t1"), ("LastMessageId", "m1"),
            ("LastCheckId", checkId), ("LastReceivedUtc", At(2)), ("DoneAtUtc", doneAt), ("FirstSeenUtc", At(2)), ("Subject", "Hi"));
        var phone = Data((TransferSchema.MailChecks, check(1, At(3))), (TransferSchema.MailWaiting, card(1, 1, At(3, 12))));
        var pc = Data((TransferSchema.MailChecks, check(7, At(2))), (TransferSchema.MailChecks, check(8, At(4))), (TransferSchema.MailWaiting, card(5, 8, null)));

        var plan = TransferMerger.Merge(phone, pc);
        var after = Apply(phone, plan);

        Assert.Equal((1, 1), (plan.Changes(TransferSchema.MailChecks).Added, plan.Changes(TransferSchema.MailChecks).Skipped));
        Assert.Equal(2L, after.Rows(TransferSchema.MailChecks).Single(c => c["StartedAtUtc"] as string == At(4))["Id"]);
        var waiting = Assert.Single(after.Rows(TransferSchema.MailWaiting));
        Assert.Equal((2L, At(3, 12)), (waiting["LastCheckId"], waiting["DoneAtUtc"]));
    }

    [Fact]
    public void Two_devices_converge_after_importing_each_other()
    {
        var pc = Data(
            (TransferSchema.JobPostings, Posting(PostingA, "https://example.com/jobs/1")),
            (TransferSchema.Applications, Application(AppOnPc, PostingA, "Applied", At(2))),
            (TransferSchema.ApplicationEvents, Event(1, AppOnPc, "Created", At(2), toStage: "Applied")),
            (TransferSchema.LlmCalls, Row(TransferSchema.LlmCalls, ("Id", 1L), ("StartedAtUtc", At(2)), ("UseCase", "job-analysis"), ("Attempt", 1L))));
        var phone = Data(
            (TransferSchema.JobPostings, Posting(PostingB, "https://example.com/jobs/2")),
            (TransferSchema.ApplicationEvents, Event(3, AppOnPhone, "Note", At(3))),
            (TransferSchema.Profiles, Row(TransferSchema.Profiles, ("Id", 1L), ("Headline", "Phone headline"), ("UpdatedAtUtc", At(4)))));

        var phoneAfter = Apply(phone, TransferMerger.Merge(phone, pc));
        var pcAfter = Apply(pc, TransferMerger.Merge(pc, phoneAfter));

        Assert.False(TransferMerger.Merge(phoneAfter, pcAfter).HasChanges);
        Assert.False(TransferMerger.Merge(pcAfter, phoneAfter).HasChanges);
        Assert.Equal(2, pcAfter.Rows(TransferSchema.JobPostings).Count);
        Assert.Equal("Phone headline", Assert.Single(pcAfter.Rows(TransferSchema.Profiles))["Headline"]);
    }

    [Fact]
    public void Rows_whose_parent_is_missing_are_skipped_and_optional_references_are_cleared()
    {
        var file = Data(
            (TransferSchema.ApplicationContacts, Row(TransferSchema.ApplicationContacts, ("Id", 1L), ("ApplicationId", AppOnPc), ("CreatedAtUtc", At(1)), ("Name", "Ana"))),
            (TransferSchema.StarStories, Row(TransferSchema.StarStories, ("Id", 1L), ("JobPostingId", PostingA), ("CreatedAtUtc", At(1)), ("UpdatedAtUtc", At(1)),
                ("MasterCvVersion", 1L), ("Json", "{}"))));

        var plan = TransferMerger.Merge(TransferData.Empty(), file);

        Assert.Equal(1, plan.Changes(TransferSchema.ApplicationContacts).Skipped);
        Assert.Null(Assert.Single(plan.Changes(TransferSchema.StarStories).Changes.Inserts)["JobPostingId"]);
    }
}
