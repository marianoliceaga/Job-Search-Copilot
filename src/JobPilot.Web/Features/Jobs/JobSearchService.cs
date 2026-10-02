using JobPilot.Web.Features.Jobs.Sources;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Features.Jobs;

public sealed record SourceRunReport(string SourceKey, string SourceName, SourceRunStatus Status, string Message, IngestionResult? Ingestion);

public sealed record SearchProgress(int Completed, int Total, SourceRunReport? Last, string? Current);

public sealed record SourceStatus(string SourceKey, string DisplayName, string TermsSummary, bool Enabled, DateTime? LastRunUtc,
    SourceRunStatus? LastStatus, string? LastMessage, DateTime? NextAllowedUtc);

/// <summary>
/// Runs a search over the enabled sources when the owner presses "Buscar" (no background process). Each source is
/// skipped while its minimum interval since the previous request has not elapsed, as its terms ask.
/// </summary>
public sealed partial class JobSearchService(
    IEnumerable<IJobSource> sources,
    IOptions<JobSourceOptions> options,
    JobPreferencesStore preferencesStore,
    FollowedCompanyStore companies,
    JobIngestion ingestion,
    IDbContextFactory<JobPilotDbContext> dbFactory,
    TimeProvider time,
    ILogger<JobSearchService> logger)
{
    public IReadOnlyList<IJobSource> Sources { get; } = sources.ToList();

    public async Task<IReadOnlyList<SourceStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var preferences = await preferencesStore.GetAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var statuses = new List<SourceStatus>();
        foreach (var source in Sources)
        {
            // The last real request (skipped runs never reached the source).
            var last = await LastRunAsync(db, source.Key, includeSkipped: false, cancellationToken);
            statuses.Add(new SourceStatus(source.Key, source.DisplayName, source.TermsSummary,
                !preferences.DisabledSources.Contains(source.Key, StringComparer.OrdinalIgnoreCase),
                last?.StartedAtUtc, last?.Status, last?.Message, await NextAllowedAsync(db, source.Key, cancellationToken)));
        }

        return statuses;
    }

    public async Task<IReadOnlyList<SourceRunReport>> RunAsync(IProgress<SearchProgress>? progress, CancellationToken cancellationToken)
    {
        var preferences = await preferencesStore.GetAsync(cancellationToken);
        var followed = await companies.GetAllAsync(cancellationToken);
        var context = new SourceFetchContext(followed, preferences.Keywords);
        var enabled = Sources.Where(s => !preferences.DisabledSources.Contains(s.Key, StringComparer.OrdinalIgnoreCase)).ToList();
        var reports = new List<SourceRunReport>();

        foreach (var source in enabled)
        {
            progress?.Report(new SearchProgress(reports.Count, enabled.Count, reports.LastOrDefault(), source.DisplayName));
            cancellationToken.ThrowIfCancellationRequested();
            reports.Add(await RunSourceAsync(source, context, preferences, cancellationToken));
        }

        progress?.Report(new SearchProgress(reports.Count, enabled.Count, reports.LastOrDefault(), null));
        return reports;
    }

    private async Task<SourceRunReport> RunSourceAsync(IJobSource source, SourceFetchContext context, Domain.Jobs.JobSearchPreferences preferences, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        var nextAllowed = await NextAllowedAsync(db, source.Key, cancellationToken);
        if (nextAllowed > now)
        {
            var message = $"Se respeta el intervalo mínimo de la fuente: próxima consulta a las {nextAllowed.Value.ToLocalTime():HH:mm}.";
            await SaveRunAsync(db, new SourceRun { SourceKey = source.Key, StartedAtUtc = now, FinishedAtUtc = now, Status = SourceRunStatus.Skipped, Message = message });
            return new SourceRunReport(source.Key, source.DisplayName, SourceRunStatus.Skipped, message, null);
        }

        var run = new SourceRun { SourceKey = source.Key, StartedAtUtc = now, Status = SourceRunStatus.Failed };
        try
        {
            var fetched = await source.FetchAsync(context, cancellationToken);
            var result = await ingestion.IngestAsync(fetched.Jobs, preferences, applyFilters: true, cancellationToken);
            run.Status = SourceRunStatus.Succeeded;
            run.Fetched = result.Received;
            run.Matched = result.Matched;
            run.Added = result.Added;
            run.Updated = result.Updated;
            run.Merged = result.Merged;
            var summary = $"{result.Received} leídas, {result.Matched} coinciden con tus palabras clave: {result.Added} nuevas, {result.Updated} actualizadas, {result.Merged} duplicadas de otra fuente.";
            run.Message = fetched.Warnings.Count == 0 ? summary : summary + " " + string.Join(" ", fetched.Warnings);
            return new SourceRunReport(source.Key, source.DisplayName, SourceRunStatus.Succeeded, run.Message, result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            run.Status = SourceRunStatus.Cancelled;
            run.Message = "Cancelado por el usuario.";
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or System.Xml.XmlException
            or TaskCanceledException or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            LogSourceFailed(ex, source.Key);
            run.Message = $"No se pudo leer la fuente: {ex.Message}";
            return new SourceRunReport(source.Key, source.DisplayName, SourceRunStatus.Failed, run.Message, null);
        }
        finally
        {
            run.FinishedAtUtc = time.GetUtcNow().UtcDateTime;
            await SaveRunAsync(db, run);
        }
    }

    /// <summary>Requests count toward the interval whether they succeeded or failed; skipped runs did not hit the source.</summary>
    private async Task<DateTime?> NextAllowedAsync(JobPilotDbContext db, string sourceKey, CancellationToken cancellationToken)
    {
        var last = await LastRunAsync(db, sourceKey, includeSkipped: false, cancellationToken);
        return last?.StartedAtUtc.AddMinutes(options.Value.For(sourceKey).MinIntervalMinutes);
    }

    private static Task<SourceRun?> LastRunAsync(JobPilotDbContext db, string sourceKey, bool includeSkipped, CancellationToken cancellationToken) =>
        db.SourceRuns.AsNoTracking()
            .Where(r => r.SourceKey == sourceKey && (includeSkipped || (r.Status != SourceRunStatus.Skipped && r.Status != SourceRunStatus.Cancelled)))
            .OrderByDescending(r => r.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task SaveRunAsync(JobPilotDbContext db, SourceRun run)
    {
        if (run.Message is { Length: > 4000 })
        {
            run.Message = run.Message[..4000];
        }

        db.SourceRuns.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Job source {SourceKey} failed.")]
    private partial void LogSourceFailed(Exception exception, string sourceKey);
}
