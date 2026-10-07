using System.Text.Json;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Outreach;

public sealed record OutreachHistoryItem(OutreachRecord Record, OutreachDraftSet Drafts, string? Company, string? Offer);

/// <summary>Outreach drafts history. Marking a draft as sent only records what the owner sent themselves.</summary>
public sealed class OutreachStore(IDbContextFactory<MobileDbContext> dbFactory, ApplicationStore applications, TimeProvider time)
{
    public async Task<IReadOnlyList<OutreachHistoryItem>> RecentAsync(int take, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var records = await db.OutreachMessages.AsNoTracking().OrderByDescending(m => m.CreatedAtUtc).Take(take).ToListAsync(cancellationToken);
        var companyIds = records.Where(r => r.CompanyId is not null).Select(r => r.CompanyId!.Value).Distinct().ToList();
        var postingIds = records.Where(r => r.JobPostingId is not null).Select(r => r.JobPostingId!.Value).Distinct().ToList();
        var companies = await db.Companies.AsNoTracking().Where(c => companyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var postings = await db.JobPostings.AsNoTracking().Where(p => postingIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => $"{p.Title} — {p.Company}", cancellationToken);
        return records.Select(r => new OutreachHistoryItem(
            r,
            Read(r),
            r.CompanyId is { } c ? companies.GetValueOrDefault(c) : null,
            r.JobPostingId is { } p ? postings.GetValueOrDefault(p) : null)).ToList();
    }

    /// <summary>Records the text the owner sent and, when the offer is in the pipeline, adds it to its timeline.</summary>
    public async Task MarkSentAsync(long id, string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.OutreachMessages.FirstAsync(m => m.Id == id, cancellationToken);
        record.SentText = text.Trim();
        record.SentAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        if (record.JobPostingId is { } postingId && await applications.FindByPostingAsync(postingId, cancellationToken) is { } applicationId)
        {
            await applications.RecordMessageSentAsync(applicationId, $"{OutreachFormat.Channel(record.Channel)}\n\n{record.SentText}", cancellationToken);
        }
    }

    public static OutreachDraftSet Read(OutreachRecord record) =>
        JsonSerializer.Deserialize<OutreachDraftSet>(record?.Json ?? "{}", DomainJson.Options) ?? new OutreachDraftSet([], null);
}

public static class OutreachFormat
{
    public static string Channel(OutreachChannel channel) => channel switch
    {
        OutreachChannel.LinkedInConnection => "Nota de invitación de LinkedIn",
        OutreachChannel.LinkedInMessage => "Mensaje de LinkedIn",
        _ => "E-mail",
    };

    public static string Tone(OutreachTone tone) => tone switch
    {
        OutreachTone.Formal => "Formal",
        OutreachTone.Friendly => "Cordial",
        _ => "Directo",
    };

    public static string Length(OutreachLength length) => length == OutreachLength.Short ? "Corto" : "Medio";
}
