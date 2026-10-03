using System.ComponentModel;

namespace JobPilot.Web.Features.Jobs.Analysis;

// Response schema of the job-analysis use case. Explanations are written in Spanish for the owner; requirements
// and quotes stay in the posting's language.

public enum RequirementStatus { Met, Partial, NotMet }

public enum ContractModel { Contractor, Eor, Employee, Unknown }

public enum Certainty { Stated, Inferred, Unknown }

public enum EligibilityVerdict { Eligible, NotEligible, Uncertain }

public sealed record JobAnalysisResponse(
    [property: Description("Fit of the candidate for this offer, 0-100, based only on the candidate data provided.")] int FitScore,
    [property: Description("2-4 sentences in Spanish (Rioplatense) explaining the fit score.")] string FitJustification,
    [property: Description("Each requirement of the posting with the candidate's status.")] List<RequirementAssessment> Requirements,
    [property: Description("Keywords and technologies of the posting that do not appear in the candidate's CV.")] List<string> MissingKeywords,
    CompensationFinding Compensation,
    ContractFinding Contract,
    EligibilityFinding Eligibility,
    [property: Description("Warning signs in the posting (vague pay, unrealistic demands, scam signs, unpaid tests...). Empty if none.")] List<RedFlag> RedFlags,
    [property: Description("2-3 sentence summary in Spanish of the offer and the main reasons to apply or not.")] string Summary);

public sealed record RequirementAssessment(
    [property: Description("The requirement as stated in the posting (short).")] string Requirement,
    RequirementStatus Status,
    [property: Description("In Spanish: which part of the CV supports it, or what is missing.")] string Evidence);

public sealed record CompensationFinding(
    [property: Description("The sentence of the posting that states the pay, copied verbatim; null when the posting states no pay. Never estimate.")] string? Quote,
    [property: Description("Minimum amount written in the quote as a plain number, or null.")] decimal? Min,
    [property: Description("Maximum amount written in the quote as a plain number, or null.")] decimal? Max,
    [property: Description("ISO 4217 currency of the quoted amounts, or null.")] string? Currency,
    [property: Description("year, month, week, day or hour; null if not stated.")] string? Period);

public sealed record ContractFinding(
    ContractModel Model,
    [property: Description("Stated when the posting says it explicitly (then quote it), Inferred when deduced, Unknown otherwise.")] Certainty Certainty,
    [property: Description("Verbatim sentence of the posting supporting the model, or null.")] string? Quote,
    [property: Description("In Spanish: short explanation, especially when inferred or unknown.")] string Explanation);

public sealed record EligibilityFinding(
    [property: Description("Whether a candidate working remotely from the candidate's country can apply.")] EligibilityVerdict Verdict,
    [property: Description("Verbatim sentence of the posting about location/eligibility, or null.")] string? Quote,
    [property: Description("In Spanish: reasoning, marking clearly what is uncertain.")] string Reasoning);

public sealed record RedFlag(
    [property: Description("In Spanish: the warning sign.")] string Flag,
    [property: Description("Verbatim sentence of the posting that shows it, or null.")] string? Quote);

/// <summary>Where the compensation used for ranking comes from.</summary>
public enum CompensationOrigin { NotInformed, Source, PostingQuote }

/// <summary>Analysis after the app's deterministic checks; this is what is stored and shown.</summary>
public sealed record VerifiedJobAnalysis(
    JobAnalysisResponse Analysis,
    CompensationOrigin CompensationOrigin,
    IReadOnlyList<string> VerificationNotes);
