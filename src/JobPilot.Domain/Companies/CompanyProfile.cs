using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Companies;

public enum CompanySourceKind
{
    /// <summary>A public page Gemini reads with its URL context tool.</summary>
    Url,

    /// <summary>Text the owner pasted (optionally with the URL it came from).</summary>
    Text,
}

/// <summary>Where the facts of a company profile may come from. Added by the owner; third-party content.</summary>
public sealed class CompanySource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public CompanySourceKind Kind { get; set; }

    /// <summary>The page to read (Url) or where the pasted text came from (Text, optional).</summary>
    public string? Url { get; set; }

    public string? Label { get; set; }

    /// <summary>Pasted text (Text sources only).</summary>
    public string? Text { get; set; }

    public DateTime AddedAtUtc { get; set; }
}

public enum CompanyFactCategory
{
    Overview,
    Product,
    Size,
    Funding,
    Technology,
    Culture,
    RemoteWork,
    Hiring,
    News,
    Other,
}

public enum FactVerification
{
    /// <summary>The quote was found in text the owner pasted.</summary>
    QuoteFound,

    /// <summary>Gemini reported reading the cited URL successfully (the quote cannot be checked locally).</summary>
    UrlRead,

    /// <summary>Shown as "no verificado".</summary>
    Unverified,
}

/// <summary>One statement about a company with the source it was taken from.</summary>
public sealed class CompanyFact
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public CompanyFactCategory Category { get; set; }

    /// <summary>The fact, in Spanish.</summary>
    public required string Statement { get; set; }

    /// <summary>Verbatim quote from the source.</summary>
    public string? Quote { get; set; }

    public Guid? SourceId { get; set; }

    /// <summary>URL of the source, always kept with the fact (null only for pasted text without an origin URL).</summary>
    public string? SourceUrl { get; set; }

    public FactVerification Verification { get; set; }

    /// <summary>Why the fact is not verified (Spanish).</summary>
    public string? VerificationNote { get; set; }

    public bool IsVerified => Verification != FactVerification.Unverified;
}

/// <summary>A company profile built from sources the owner chose. Stored locally.</summary>
public sealed class CompanyProfile
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary><see cref="JobFingerprint.CompanyKey"/> of the name: links the profile to offers of the company.</summary>
    public required string NameKey { get; set; }

    public string? Website { get; set; }

    /// <summary>Owner's own notes (never sent to third parties unless included in a prompt the owner reviews).</summary>
    public string? Notes { get; set; }

    public List<CompanySource> Sources { get; set; } = [];

    public List<CompanyFact> Facts { get; set; } = [];

    /// <summary>Topics the last research could not find in the sources (Spanish).</summary>
    public List<string> Gaps { get; set; } = [];

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ResearchedAtUtc { get; set; }

    public string? PromptId { get; set; }

    public string? Model { get; set; }

    public static CompanyProfile Create(string name, string? website, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new CompanyProfile
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            NameKey = JobFingerprint.CompanyKey(name),
            Website = string.IsNullOrWhiteSpace(website) ? null : website.Trim(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
        };
    }
}
