namespace JobPilot.Domain.Pipeline;

/// <param name="Visits">Completed stays in the stage (the current stage of each application is not counted).</param>
public sealed record StageTime(ApplicationStage Stage, int Visits, double? AverageDays);

/// <param name="Applied">Applications that reached "Postulada" (or an interview/offer).</param>
/// <param name="Responded">Applied ones the company answered: interview, offer, rejection or a recorded response.</param>
/// <param name="ResponseRate">Responded / Applied; null without applications.</param>
public sealed record PipelineMetricsResult(
    int Total,
    IReadOnlyDictionary<ApplicationStage, int> ByStage,
    int Applied,
    int Responded,
    double? ResponseRate,
    double? AverageDaysToFirstResponse,
    IReadOnlyList<StageTime> TimeInStage);

public static class PipelineMetrics
{
    public static PipelineMetricsResult Compute(IReadOnlyCollection<JobApplication> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        var byStage = Enum.GetValues<ApplicationStage>().ToDictionary(s => s, s => applications.Count(a => a.Stage == s));

        var applied = 0;
        var responseDays = new List<double>();
        var durations = Enum.GetValues<ApplicationStage>().ToDictionary(s => s, _ => new List<double>());
        foreach (var application in applications)
        {
            var visits = application.StageVisits();
            foreach (var visit in visits.Where(v => v.LeftAtUtc is not null))
            {
                durations[visit.Stage].Add((visit.LeftAtUtc!.Value - visit.EnteredAtUtc).TotalDays);
            }

            var appliedIndex = visits.ToList().FindIndex(v => v.Stage.ImpliesApplied());
            if (appliedIndex < 0)
            {
                continue;
            }

            applied++;
            var appliedVisit = visits[appliedIndex];
            var appliedAt = appliedVisit.EnteredAtUtc;
            // Entering "Entrevista" or "Oferta" directly (without "Postulada") is itself the first answer.
            var stageResponse = appliedVisit.Stage == ApplicationStage.Applied
                ? visits.Skip(appliedIndex + 1).Where(v => v.Stage.IsResponse()).Select(v => (DateTime?)v.EnteredAtUtc).FirstOrDefault()
                : appliedAt;

            var recorded = application.Events
                .Where(e => e.Kind == ApplicationEventKind.ResponseReceived && e.OccurredAtUtc >= appliedAt)
                .Select(e => (DateTime?)e.OccurredAtUtc).Min();
            var first = new[] { stageResponse, recorded }.Where(d => d is not null).Min();
            if (first is { } respondedAt)
            {
                responseDays.Add((respondedAt - appliedAt).TotalDays);
            }
        }

        return new PipelineMetricsResult(
            Total: applications.Count,
            ByStage: byStage,
            Applied: applied,
            Responded: responseDays.Count,
            ResponseRate: applied == 0 ? null : (double)responseDays.Count / applied,
            AverageDaysToFirstResponse: responseDays.Count == 0 ? null : responseDays.Average(),
            TimeInStage: durations.Select(d => new StageTime(d.Key, d.Value.Count, d.Value.Count == 0 ? null : d.Value.Average())).ToList());
    }
}
