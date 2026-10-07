using JobPilot.Domain.Jobs;
using JobPilot.Domain.Mail;
using JobPilot.Mobile.Core.Features.Jobs;
using JobPilot.Mobile.Core.Features.Jobs.Sources;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Mail;

public sealed record MailCheckOutcome(bool Succeeded, string? Error, long? CheckId);

/// <summary>The latest check and the "Te esperan" cards it left, for the Panel.</summary>
public sealed record MailBrief(MailCheckRecord Check, MailCheckRecord? LastSuccessful, IReadOnlyList<MailWaitingRecord> Waiting);

/// <summary>
/// "Actualizar correo": reads the owner's Gmail with gmail.readonly since the last successful check, sorts every thread
/// with the mail rules, turns LinkedIn alert roles into offers and stores the facts for the Panel. Runs only when the owner
/// asks; nothing is sent, labelled or marked as read, and message bodies are never stored or sent to Gemini.
/// </summary>
public sealed class MailCheckService(
    GmailClient gmail,
    GoogleAuthService auth,
    MailRulesStore rulesStore,
    JobIngestion ingestion,
    JobPreferencesStore preferences,
    IDbContextFactory<MobileDbContext> dbFactory,
    TimeProvider time)
{
    public const string SourceKey = "linkedin-alerts";
    public const string SourceName = "LinkedIn (alertas por mail)";
    private const int MaxConcurrentReads = 3;

    // One check at a time, whatever page started it.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<MailCheckOutcome> RunAsync(CancellationToken cancellationToken)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
        {
            return new MailCheckOutcome(false, "Ya hay una revisión del correo en curso.", null);
        }

        try
        {
            return await RunLockedAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<MailBrief?> GetBriefAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var latest = await db.MailChecks.AsNoTracking().OrderByDescending(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        if (latest is null)
        {
            return null;
        }

        var successful = latest.Succeeded
            ? latest
            : await db.MailChecks.AsNoTracking().Where(c => c.Succeeded).OrderByDescending(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        var waiting = successful is null
            ? []
            : await db.MailWaiting.AsNoTracking().Where(w => w.LastCheckId == successful.Id && w.DoneAtUtc == null).ToListAsync(cancellationToken);
        return new MailBrief(latest, successful, waiting.OrderBy(w => w.Tone).ThenBy(w => w.RailUtc).ToList());
    }

    /// <summary>"Listo": hides the card until a newer message arrives in the thread.</summary>
    public async Task MarkDoneAsync(long waitingId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        await db.MailWaiting.Where(w => w.Id == waitingId).ExecuteUpdateAsync(s => s.SetProperty(w => w.DoneAtUtc, nowUtc), cancellationToken);
    }

    /// <summary>The applications a card may belong to (contact address, company in the sender domain, subject or name).</summary>
    public static IReadOnlyList<MailApplicationCandidate> SuggestApplications(MailWaitingRecord card, IReadOnlyList<MailApplicationCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(card);
        return MailApplicationMatcher.Match(card.SenderAddress, card.SenderName, card.Subject, candidates);
    }

    private async Task<MailCheckOutcome> RunLockedAsync(CancellationToken cancellationToken)
    {
        var status = auth.Status();
        if (!status.Connected)
        {
            return new MailCheckOutcome(false, "Conectá Google para leer el correo (Configuración → Google).", null);
        }

        if (!status.CanReadMail)
        {
            return new MailCheckOutcome(false, "No diste permiso para leer el correo: volvé a conectar Google eligiendo \"Gmail y Calendar\".", null);
        }

        var rules = await rulesStore.GetAsync(cancellationToken);
        MailClassifier classifier;
        try
        {
            classifier = MailClassifier.Compile(rules);
        }
        catch (MailRulesException ex)
        {
            return new MailCheckOutcome(false, $"Las reglas de correo tienen un error: {ex.Message}", null);
        }

        var started = time.GetUtcNow();
        var zone = time.LocalTimeZone;
        var window = MailWindow.Compute(started, await LastSuccessfulStartAsync(cancellationToken), rules.Gmail.LookbackCapDays);
        var check = new MailCheckRecord { StartedAtUtc = started.UtcDateTime, WindowSinceUtc = window.Since.UtcDateTime };

        var ids = new List<string>();
        foreach (var (name, query) in new[] { ("bandeja de entrada", window.Apply(rules.Gmail.InboxQuery)), ("seguimiento", rules.Gmail.FollowUpQuery) })
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                continue;
            }

            var page = await gmail.ListThreadIdsAsync(query, rules.Gmail.MaxThreadsPerQuery, cancellationToken);
            if (page.Value is null)
            {
                return await SaveFailureAsync(check, page.Error!, cancellationToken);
            }

            if (page.Value.Truncated)
            {
                check.Warnings.Add($"La búsqueda de {name} tiene más de {rules.Gmail.MaxThreadsPerQuery} hilos: se leyeron los más recientes.");
            }

            ids.AddRange(page.Value.Ids);
        }

        var (threads, failures) = await ReadThreadsAsync(ids.Distinct(StringComparer.Ordinal).ToList(), status.Email, zone, cancellationToken);
        var result = classifier.Classify(threads, failures, LinkedInAlertParser.Parse, started, zone);

        check.Warnings.AddRange(result.Warnings);
        check.ThreadsRead = result.Counts.ThreadsRead;
        check.Waiting = result.Counts.Waiting;
        check.AlertThreads = result.Counts.Roles;
        check.Filtered = result.Counts.Filtered;
        check.Listings = result.Roles.Listings;
        check.FilteredChips = result.Filtered.ToList();
        check.RoleFilters = result.RoleFilters.ToList();

        var jobPreferences = await preferences.GetAsync(cancellationToken);
        foreach (var role in result.Roles.Shown)
        {
            var ingested = await ingestion.IngestAsync([ToSourcedJob(role)], jobPreferences, applyFilters: false, cancellationToken);
            check.OffersAdded += ingested.Added;
            var best = role.Best;
            check.Roles.Add(new MailRoleSummary(ingested.PostingIds[0], best.Title, best.Company, string.Join(" / ", role.Locations), best.Workplace,
                role.Fit, role.Score, role.Why, role.PayMonthlyUsd, role.Listings.Count, ingested.Added > 0));
        }

        check.Succeeded = true;
        check.FinishedAtUtc = time.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.MailChecks.Add(check);
        await db.SaveChangesAsync(cancellationToken);
        await UpsertWaitingAsync(db, check.Id, result.Waiting, cancellationToken);
        return new MailCheckOutcome(true, null, check.Id);
    }

    private async Task<(List<MailThreadInfo> Threads, List<MailFetchFailure> Failures)> ReadThreadsAsync(
        IReadOnlyList<string> ids, string? ownEmail, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentReads);
        var reads = ids.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (Id: id, Result: await gmail.GetFullThreadAsync(id, ownEmail, zone, cancellationToken));
            }
            finally
            {
                gate.Release();
            }
        });
        var results = await Task.WhenAll(reads);
        return (
            results.Where(r => r.Result.Value is not null).Select(r => r.Result.Value!).ToList(),
            results.Where(r => r.Result.Value is null).Select(r => new MailFetchFailure(r.Id, r.Result.Error!)).ToList());
    }

    private static SourcedJob ToSourcedJob(ScoredRole role)
    {
        var best = role.Best;
        var workplace = best.Workplace switch
        {
            "remote" => "Remoto",
            "hybrid" => "Híbrido",
            "onsite" => "Presencial",
            _ => null,
        };
        var location = string.Join(" / ", role.Locations);
        return new SourcedJob
        {
            SourceKey = SourceKey,
            SourceName = SourceName,
            SourceJobId = best.JobId,
            Title = best.Title,
            Company = best.Company,
            Url = best.Url,
            Location = workplace is null ? NullIfEmpty(location) : NullIfEmpty(location) is { } l ? $"{l} ({workplace})" : workplace,
            Tags = best.Flags,
            PublishedAtUtc = best.Received.UtcDateTime,
            SalaryMin = best.Pay?.Low,
            SalaryMax = best.Pay?.High,
            SalaryCurrency = best.Pay?.Currency,
            SalaryPeriod = best.Pay?.Period switch
            {
                "year" => SalaryPeriod.Year,
                "month" => SalaryPeriod.Month,
                "hour" => SalaryPeriod.Hour,
                _ => null,
            },
            SalaryText = best.Pay?.Text,
        };
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private async Task UpsertWaitingAsync(MobileDbContext db, long checkId, IReadOnlyList<WaitingItem> items, CancellationToken cancellationToken)
    {
        var threadIds = items.Select(i => i.ThreadId).ToList();
        var existing = await db.MailWaiting.Where(w => threadIds.Contains(w.ThreadId)).ToDictionaryAsync(w => w.ThreadId, StringComparer.Ordinal, cancellationToken);
        var nowUtc = time.GetUtcNow().UtcDateTime;
        foreach (var item in items)
        {
            if (!existing.TryGetValue(item.ThreadId, out var record))
            {
                record = new MailWaitingRecord
                {
                    ThreadId = item.ThreadId,
                    LastMessageId = item.LastMessageId,
                    RuleId = item.RuleId,
                    Pill = item.Pill,
                    Subject = item.Subject,
                    SenderName = item.SenderName,
                    SenderAddress = item.SenderAddress,
                    Timeline = item.Timeline,
                    NextAction = item.NextAction,
                    RailNote = item.RailNote,
                    FirstSeenUtc = nowUtc,
                };
                db.MailWaiting.Add(record);
            }
            else if (record.LastMessageId != item.LastMessageId)
            {
                // A newer message: the owner has something new to look at.
                record.DoneAtUtc = null;
            }

            record.LastMessageId = item.LastMessageId;
            record.LastCheckId = checkId;
            record.RuleId = item.RuleId;
            record.Pill = Truncate(item.Pill, 100);
            record.Tone = item.Tone;
            record.Subject = Truncate(item.Subject, 1000);
            record.SenderName = Truncate(item.SenderName, 300);
            record.SenderAddress = Truncate(item.SenderAddress, 300);
            record.Timeline = Truncate(item.Timeline, 2000);
            record.NextAction = Truncate(item.NextAction, 1000);
            record.LastReceivedUtc = item.LastReceived.UtcDateTime;
            record.RailUtc = item.RailTime.UtcDateTime;
            record.RailNote = item.RailNote;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;

    private async Task<DateTimeOffset?> LastSuccessfulStartAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var last = await db.MailChecks.AsNoTracking().Where(c => c.Succeeded).OrderByDescending(c => c.Id).Select(c => (DateTime?)c.StartedAtUtc).FirstOrDefaultAsync(cancellationToken);
        return last is { } value ? new DateTimeOffset(value, TimeSpan.Zero) : null;
    }

    private async Task<MailCheckOutcome> SaveFailureAsync(MailCheckRecord check, string error, CancellationToken cancellationToken)
    {
        check.Succeeded = false;
        check.Error = error;
        check.FinishedAtUtc = time.GetUtcNow().UtcDateTime;
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.MailChecks.Add(check);
        await db.SaveChangesAsync(cancellationToken);
        return new MailCheckOutcome(false, error, check.Id);
    }
}
