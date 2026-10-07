using JobPilot.Mobile.Core.Features.Transfer;

namespace JobPilot.Mobile.Tests.Features.Transfer;

/// <summary>Builds raw rows the way EF Core stores them in SQLite (dates and GUIDs as text) and applies plans in memory.</summary>
internal static class TransferTestData
{
    public const string PostingA = "0A0A0A0A-0000-0000-0000-000000000001";
    public const string PostingB = "0B0B0B0B-0000-0000-0000-000000000002";

    public static string At(int day, int hour = 10) => $"2026-10-{day:00} {hour:00}:00:00";

    public static TransferRow Row(string table, params (string Column, object? Value)[] values)
    {
        var definition = TransferSchema.Get(table);
        var row = new TransferRow();
        foreach (var column in definition.Columns)
        {
            row[column] = null;
        }

        foreach (var (column, value) in values)
        {
            Assert.Contains(column, definition.Columns);
            row[column] = TransferValues.Normalize(value);
        }

        return row;
    }

    public static TransferData Data(params (string Table, TransferRow Row)[] rows)
    {
        var data = TransferData.Empty();
        foreach (var (table, row) in rows)
        {
            data.Tables[table].Add(row);
        }

        return data;
    }

    public static TransferRow Posting(string id, string urlKey, string? description = "", string lastSeen = "2026-10-01 10:00:00") =>
        Row(TransferSchema.JobPostings, ("Id", id), ("UrlKey", urlKey), ("Url", urlKey), ("Title", "Backend Developer"), ("Company", "Northwind"),
            ("Description", description), ("FirstSeenAtUtc", At(1)), ("LastSeenAtUtc", lastSeen), ("Sightings", "[]"), ("IsDiscarded", 0L),
            ("SourceKey", "remotive"), ("SourceName", "Remotive"), ("Fingerprint", "northwind|backend developer"));

    public static TransferRow Application(string id, string postingId, string stage, string changedAt) =>
        Row(TransferSchema.Applications, ("Id", id), ("JobPostingId", postingId), ("Stage", stage), ("CreatedAtUtc", At(1)), ("StageChangedAtUtc", changedAt));

    public static TransferRow Event(long id, string applicationId, string kind, string at, string? toStage = null, string? text = null, string? scheduledAt = null) =>
        Row(TransferSchema.ApplicationEvents, ("Id", id), ("ApplicationId", applicationId), ("Kind", kind), ("OccurredAtUtc", at), ("ToStage", toStage),
            ("Text", text), ("ScheduledAtUtc", scheduledAt));

    public static TransferRow Deleted(long id, string table, string identityJson, string at) =>
        Row(TransferSchema.DeletedRecords, ("Id", id), ("TableName", table), ("Identity", identityJson), ("DeletedAtUtc", at));

    /// <summary>What the database holds after the plan is applied (deletes cascade like the apps' foreign keys).</summary>
    public static TransferData Apply(TransferData existing, TransferPlan plan)
    {
        var result = new TransferData();
        foreach (var table in TransferSchema.Tables)
        {
            result.Tables[table.Name] = existing.Rows(table.Name).Select(r => r.Copy()).ToList();
        }

        foreach (var changes in plan.Tables.Reverse())
        {
            var rows = result.Tables[changes.Table.Name];
            foreach (var key in changes.Deletes)
            {
                var removed = rows.Where(r => TransferValues.AreEqual(r[changes.Table.Key], key)).ToList();
                rows.RemoveAll(removed.Contains);
                if (changes.Table.Name == TransferSchema.Applications)
                {
                    foreach (var child in new[] { TransferSchema.ApplicationEvents, TransferSchema.ApplicationContacts, TransferSchema.ApplicationMailThreads })
                    {
                        result.Tables[child].RemoveAll(r => TransferValues.AreEqual(r["ApplicationId"], key));
                    }
                }
            }
        }

        foreach (var changes in plan.Tables)
        {
            var rows = result.Tables[changes.Table.Name];
            foreach (var update in changes.Updates)
            {
                var index = rows.FindIndex(r => TransferValues.AreEqual(r[changes.Table.Key], update[changes.Table.Key]));
                Assert.True(index >= 0, $"Update of a missing {changes.Table.Name} row.");
                rows[index] = update.Copy();
            }

            foreach (var insert in changes.Inserts)
            {
                Assert.DoesNotContain(rows, r => TransferValues.AreEqual(r[changes.Table.Key], insert[changes.Table.Key]));
                rows.Add(insert.Copy());
            }
        }

        return result;
    }

    public static TransferChangesView Changes(this TransferPlan plan, string table) => new(plan.Tables.Single(t => t.Table.Name == table));
}

internal sealed record TransferChangesView(TransferTableChanges Changes)
{
    public int Added => Changes.Inserts.Count;

    public int Updated => Changes.Updates.Count;

    public int Deleted => Changes.Deletes.Count;

    public int Skipped => Changes.Skipped;
}
