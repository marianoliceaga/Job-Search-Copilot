using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;

namespace JobPilot.Mobile.Core.Features.Jobs.Analysis;

/// <summary>
/// Deterministic checks of an LLM analysis against the posting and the CV. Values that cannot be traced are removed
/// or downgraded, never kept silently: "no informada" instead of a guessed salary.
/// </summary>
public static class AnalysisVerifier
{
    public static VerifiedJobAnalysis Verify(JobAnalysisResponse analysis, JobPosting posting, string cvText)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(posting);
        var postingText = posting.SearchableText;
        var notes = new List<string>();

        // Compensation: the source's structured data wins; otherwise the LLM must quote the posting.
        var compensation = analysis.Compensation;
        CompensationOrigin origin;
        if (posting.SalaryMin is not null || posting.SalaryMax is not null)
        {
            origin = CompensationOrigin.Source;
            compensation = new CompensationFinding(posting.SalaryText, posting.SalaryMin, posting.SalaryMax, posting.SalaryCurrency,
                posting.SalaryPeriod?.ToString().ToLowerInvariant());
        }
        else if (compensation.Quote is null || !EvidenceCheck.QuoteFound(compensation.Quote, postingText))
        {
            if (compensation.Quote is not null || compensation.Min is not null || compensation.Max is not null)
            {
                notes.Add("Compensación descartada: el texto citado no aparece en la oferta. Queda como no informada.");
            }

            origin = CompensationOrigin.NotInformed;
            compensation = new CompensationFinding(null, null, null, null, null);
        }
        else
        {
            origin = CompensationOrigin.PostingQuote;
            var min = compensation.Min is { } a && EvidenceCheck.AmountInQuote(a, compensation.Quote) ? a : (decimal?)null;
            var max = compensation.Max is { } b && EvidenceCheck.AmountInQuote(b, compensation.Quote) ? b : (decimal?)null;
            if (min != compensation.Min || max != compensation.Max)
            {
                notes.Add("Se descartaron montos de compensación que no figuran en el texto citado.");
            }

            compensation = compensation with { Min = min, Max = max };
        }

        // Contract model: a "stated" model needs a quote that exists.
        var contract = analysis.Contract;
        if (contract.Quote is not null && !EvidenceCheck.QuoteFound(contract.Quote, postingText))
        {
            notes.Add("Modalidad de contratación: la cita no aparece en la oferta; se marca como inferida.");
            contract = contract with { Quote = null, Certainty = contract.Certainty == Certainty.Stated ? Certainty.Inferred : contract.Certainty };
        }
        else if (contract.Certainty == Certainty.Stated && contract.Quote is null)
        {
            contract = contract with { Certainty = Certainty.Inferred };
        }

        var eligibility = analysis.Eligibility;
        if (eligibility.Quote is not null && !EvidenceCheck.QuoteFound(eligibility.Quote, postingText))
        {
            notes.Add("Elegibilidad: la cita no aparece en la oferta y se quitó.");
            eligibility = eligibility with { Quote = null };
        }

        var redFlags = analysis.RedFlags
            .Select(flag => flag.Quote is not null && !EvidenceCheck.QuoteFound(flag.Quote, postingText) ? flag with { Quote = null } : flag)
            .ToList();

        // Missing keywords must be in the posting and absent from the CV.
        var normalizedCv = TextNormalizer.Normalize(cvText);
        var missing = analysis.MissingKeywords
            .Where(k => EvidenceCheck.QuoteFound(k, postingText) && !normalizedCv.Contains(TextNormalizer.Normalize(k), StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count != analysis.MissingKeywords.Count)
        {
            notes.Add("Se quitaron keywords que no están en la oferta o que sí están en tu CV.");
        }

        var verified = analysis with
        {
            FitScore = Math.Clamp(analysis.FitScore, 0, 100),
            Compensation = compensation,
            Contract = contract,
            Eligibility = eligibility,
            RedFlags = redFlags,
            MissingKeywords = missing,
        };
        return new VerifiedJobAnalysis(verified, origin, notes);
    }
}
