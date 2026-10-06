using System.ComponentModel;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Prompts;
using JobPilot.Mobile.Core.Features.Jobs.Sources;
using JobPilot.Mobile.Core.Infrastructure.Llm;

namespace JobPilot.Mobile.Core.Features.Jobs;

// Response schema of the job-extraction use case.
public sealed record ExtractedJob(
    [property: Description("Job title exactly as written.")] string Title,
    [property: Description("Hiring company as written; \"Unknown\" only if the text does not name it.")] string Company,
    [property: Description("Location or remote restrictions as written (e.g. \"Remote - LATAM\"), or null.")] string? Location,
    [property: Description("Employment or contract type as written (e.g. \"Contractor\", \"Full-time\"), or null.")] string? EmploymentType,
    [property: Description("The salary/compensation sentence copied verbatim from the text, or null when the text states none.")] string? SalaryQuote,
    [property: Description("Minimum amount stated in salaryQuote, as a plain number (120k → 120000), or null.")] decimal? SalaryMin,
    [property: Description("Maximum amount stated in salaryQuote, as a plain number, or null.")] decimal? SalaryMax,
    [property: Description("ISO 4217 currency of the salary if stated or unambiguous from a symbol, or null.")] string? SalaryCurrency,
    [property: Description("One of: year, month, week, day, hour; null when the text does not say.")] string? SalaryPeriod,
    [property: Description("Technologies explicitly named in the text.")] List<string> Technologies);

public enum UrlImportOutcome
{
    Imported,
    NeedsText,
    NotFound,
    Failed,
}

public sealed record UrlImportResult(UrlImportOutcome Outcome, string Message, Guid? PostingId = null);

public sealed record PreparedJobExtraction(string Text, string? Url, RenderedPrompt Prompt);

/// <param name="Warnings">Values removed because they could not be found in the text.</param>
public sealed record JobExtractionResult(LlmResult<ExtractedJob> Llm, SourcedJob? Draft, IReadOnlyList<string> Warnings);

/// <summary>
/// Manual entry: a URL of a public ATS job is read through the ATS API; for any other site the owner pastes the text
/// (sites like LinkedIn forbid automated access) and the LLM only extracts fields, which are verified against the text.
/// </summary>
public sealed class ManualJobImport(
    IEnumerable<IJobSource> sources,
    JobIngestion ingestion,
    JobPreferencesStore preferences,
    PromptLibrary prompts,
    ILlmClient llm)
{
    public const string ManualSourceKey = "manual";
    public const int MaxTextLength = 40_000;

    private static readonly string[] ForbiddenHosts = ["linkedin.com", "indeed.com", "glassdoor.com", "wellfound.com", "angel.co"];

    public async Task<UrlImportResult> ImportFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return new(UrlImportOutcome.Failed, "La URL no es válida.");
        }

        var host = uri.Host.ToLowerInvariant();
        if (ForbiddenHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
        {
            return new(UrlImportOutcome.NeedsText,
                $"{uri.Host} no permite el acceso automatizado en sus términos. Copiá el texto de la oferta y pegalo abajo.");
        }

        var reference = AtsUrlParser.Parse(url);
        if (reference?.JobId is null)
        {
            return new(UrlImportOutcome.NeedsText,
                "Esta URL no es de un ATS con API pública (Greenhouse, Lever o Ashby). Pegá el texto de la oferta abajo; la URL queda como enlace.");
        }

        var source = sources.OfType<AtsSource>().Single(s => s.Ats == reference.Ats);
        var (jobs, warning) = await source.FetchBoardAsync(reference.BoardToken, reference.BoardToken, cancellationToken);
        var job = jobs.FirstOrDefault(j => string.Equals(j.SourceJobId, reference.JobId, StringComparison.OrdinalIgnoreCase));
        if (job is null)
        {
            return new(UrlImportOutcome.NotFound, warning ?? "La oferta ya no está publicada en ese tablero.");
        }

        var result = await ingestion.IngestAsync([job], await preferences.GetAsync(cancellationToken), applyFilters: false, cancellationToken);
        return new(UrlImportOutcome.Imported, $"Oferta importada desde {source.DisplayName}.", result.PostingIds.Single());
    }

    public PreparedJobExtraction PrepareExtraction(string text, string? url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var trimmed = text.Trim();
        if (trimmed.Length > MaxTextLength)
        {
            throw new ArgumentException($"El texto supera los {MaxTextLength:N0} caracteres.", nameof(text));
        }

        var prompt = prompts.Get(LlmUseCases.JobExtraction).Render(new Dictionary<string, string> { ["posting"] = trimmed });
        return new PreparedJobExtraction(trimmed, string.IsNullOrWhiteSpace(url) ? null : url.Trim(), prompt);
    }

    public async Task<JobExtractionResult> ExtractAsync(PreparedJobExtraction prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<ExtractedJob>(prepared.Prompt) { Validate = Validate }, cancellationToken);
        if (!result.IsSuccess)
        {
            return new JobExtractionResult(result, null, []);
        }

        var (draft, warnings) = ToDraft(result.Value!, prepared);
        return new JobExtractionResult(result, draft, warnings);
    }

    /// <summary>Stores a reviewed manual offer.</summary>
    public async Task<Guid> SaveAsync(SourcedJob draft, CancellationToken cancellationToken)
    {
        var result = await ingestion.IngestAsync([draft], await preferences.GetAsync(cancellationToken), applyFilters: false, cancellationToken);
        return result.PostingIds.Single();
    }

    internal static IReadOnlyList<string> Validate(ExtractedJob job)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(job.Title))
        {
            problems.Add("title is empty");
        }

        if (job.SalaryPeriod is not null && SourceParsing.Period(job.SalaryPeriod) is null)
        {
            problems.Add($"salaryPeriod \"{job.SalaryPeriod}\" must be year, month, week, day, hour or null");
        }

        if ((job.SalaryMin is not null || job.SalaryMax is not null) && string.IsNullOrWhiteSpace(job.SalaryQuote))
        {
            problems.Add("salaryMin/salaryMax require salaryQuote with the sentence that states them");
        }

        return problems;
    }

    internal static (SourcedJob Draft, IReadOnlyList<string> Warnings) ToDraft(ExtractedJob job, PreparedJobExtraction prepared)
    {
        var warnings = new List<string>();
        var quote = string.IsNullOrWhiteSpace(job.SalaryQuote) ? null : job.SalaryQuote.Trim();
        decimal? min = job.SalaryMin, max = job.SalaryMax;

        if (quote is not null && !EvidenceCheck.QuoteFound(quote, prepared.Text))
        {
            warnings.Add("Se descartó la compensación: el texto citado no aparece en la oferta.");
            quote = null;
            min = max = null;
        }

        if (min is { } a && !EvidenceCheck.AmountInQuote(a, quote))
        {
            warnings.Add($"Se descartó el mínimo {a:N0}: no figura en el texto de la compensación.");
            min = null;
        }

        if (max is { } b && !EvidenceCheck.AmountInQuote(b, quote))
        {
            warnings.Add($"Se descartó el máximo {b:N0}: no figura en el texto de la compensación.");
            max = null;
        }

        foreach (var (label, value) in new[] { ("El puesto", job.Title), ("La empresa", job.Company) })
        {
            if (!EvidenceCheck.QuoteFound(value, prepared.Text))
            {
                warnings.Add($"{label} \"{value}\" no aparece tal cual en el texto: revisalo.");
            }
        }

        var hasAmounts = min is not null || max is not null;
        var draft = new SourcedJob
        {
            SourceKey = ManualSourceKey,
            SourceName = "Alta manual",
            Title = job.Title.Trim(),
            Company = string.IsNullOrWhiteSpace(job.Company) ? "(sin empresa)" : job.Company.Trim(),
            Url = prepared.Url ?? $"manual:{Guid.NewGuid():N}",
            Location = job.Location,
            EmploymentType = job.EmploymentType,
            Tags = job.Technologies.Where(t => EvidenceCheck.QuoteFound(t, prepared.Text)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            // The description is the pasted text itself, never an LLM rewrite.
            Description = prepared.Text,
            SalaryText = quote,
            SalaryMin = min,
            SalaryMax = max,
            SalaryCurrency = hasAmounts ? job.SalaryCurrency?.Trim().ToUpperInvariant() : null,
            SalaryPeriod = hasAmounts ? SourceParsing.Period(job.SalaryPeriod) : null,
        };
        return (draft, warnings);
    }
}
