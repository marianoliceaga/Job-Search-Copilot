namespace JobPilot.Domain.Mail;

/// <summary>
/// The rules that sort the owner's job-search mail (ported from JobSearchDesk, limited to job search). Stored as a
/// settings document seeded from <c>Seed/mail-rules.seed.json</c> and edited by the owner as JSON.
/// Word lists: "entrevista" matches the whole word or phrase ignoring case and accents; "re:..." is a regular expression on
/// lower-case text without accents; "$name" expands the list <c>Sets[name]</c>. Sender lists: "name@domain" is one address,
/// "domain.com" a domain and its subdomains, "re:..." a pattern on the address.
/// </summary>
public sealed record MailRules
{
    public MailGmailRules Gmail { get; init; } = new();

    public Dictionary<string, List<string>> Sets { get; init; } = [];

    /// <summary>Machine senders whatever their headers say; a sender is "human" only if not listed and not bulk mail.</summary>
    public List<string> AutomatedSenders { get; init; } = [];

    public MailLinkedInRules LinkedIn { get; init; } = new();

    /// <summary>"Te esperan": tried in order, the first match wins. Only threads whose last message is not the owner's.</summary>
    public List<WaitingRule> WaitingRules { get; init; } = [];

    /// <summary>"Filtrados": everything else, counted by category. Tried in order; keep a catch-all last.</summary>
    public List<FilterCategory> FilterCategories { get; init; } = [];

    public RoleScoringRules Scoring { get; init; } = new();
}

public sealed record MailGmailRules
{
    /// <summary>Query A. {since} becomes after:&lt;last check&gt; (at most LookbackCapDays back) or newer_than:1d the first time.</summary>
    public string InboxQuery { get; init; } = "in:inbox {since}";

    /// <summary>Query B: unread job-related mail from earlier days. Empty disables it.</summary>
    public string FollowUpQuery { get; init; } = string.Empty;

    public int MaxThreadsPerQuery { get; init; } = 50;

    public int LookbackCapDays { get; init; } = 4;
}

public sealed record MailLinkedInRules
{
    /// <summary>Only these job-alert e-mails become offers ("LinkedIn (alertas por mail)").</summary>
    public List<string> AlertSenders { get; init; } = [];

    public string TemplateHeader { get; init; } = "X-LinkedIn-Template";

    public List<string> TemplateMatches { get; init; } = [];
}

/// <summary>Conditions on a thread; all given conditions must hold. Tested on the latest message the owner did not write.</summary>
public sealed record ThreadMatch
{
    public List<string>? From { get; init; }

    public List<string>? NotFrom { get; init; }

    public List<string>? Subject { get; init; }

    /// <summary>Subject and body (first 4,000 characters).</summary>
    public List<string>? Text { get; init; }

    public List<string>? NotText { get; init; }

    public bool? HumanSender { get; init; }

    public bool? Bulk { get; init; }

    public bool? LastFromMe { get; init; }

    public bool? HasInvite { get; init; }

    public Dictionary<string, List<string>>? Headers { get; init; }
}

public enum WaitingRuleKind
{
    /// <summary>The thread matches <see cref="WaitingRule.Match"/>.</summary>
    Match,

    /// <summary>A future invite (METHOD:REQUEST) the owner has not answered in the e-mail copy.</summary>
    InviteNeedsReply,

    /// <summary>A future interview notice that asks for no reply (e.g. METHOD:PUBLISH).</summary>
    InviteUpcoming,
}

public enum MailTone
{
    Act,
    Hold,
    Ok,
}

/// <summary>
/// A "Te esperan" rule. NextAction placeholders: {subject} {sender} {senderName} {senderAddress} {received}, and for invite
/// rules {inviteTitle} {inviteWhen}.
/// </summary>
public sealed record WaitingRule
{
    public required string Id { get; init; }

    public WaitingRuleKind Kind { get; init; } = WaitingRuleKind.Match;

    public required string Pill { get; init; }

    public MailTone Tone { get; init; } = MailTone.Act;

    public ThreadMatch Match { get; init; } = new();

    public required string NextAction { get; init; }
}

public sealed record FilterCategory
{
    public required string Id { get; init; }

    public required string Chip { get; init; }

    public ThreadMatch Match { get; init; } = new();
}

/// <summary>
/// Role scoring for LinkedIn alerts. Each rule adds its weight when all its conditions hold; filters drop a role under
/// their chip unless a group reaches <see cref="RoleFilter.UnlessGroupAtLeast"/>.
/// </summary>
public sealed record RoleScoringRules
{
    public List<ScoreRule> Rules { get; init; } = [];

    public List<RoleFilter> Filters { get; init; } = [];

    public FitThresholds Thresholds { get; init; } = new();

    public string BelowLevelChip { get; init; } = "Por debajo del nivel";

    public PayRules Pay { get; init; } = new();
}

/// <summary>
/// A scoring condition. Fields: title, company, location, workplace (remote | hybrid | onsite | empty), flags, alert;
/// several fields are joined ("title,location"). Pay: atOrAboveFloor | belowFloor | unknown.
/// </summary>
public sealed record RoleCondition
{
    public string? Field { get; init; }

    public List<string>? Any { get; init; }

    public List<string>? None { get; init; }

    public bool? Empty { get; init; }

    public string? Pay { get; init; }
}

/// <summary>A scoring rule. Why placeholders: {payMonthly}.</summary>
public sealed record ScoreRule
{
    public required string Id { get; init; }

    public required string Why { get; init; }

    public required string Group { get; init; }

    public int Weight { get; init; }

    public List<RoleCondition> When { get; init; } = [];
}

public sealed record RoleFilter
{
    public required string Id { get; init; }

    public required string Chip { get; init; }

    public List<RoleCondition> When { get; init; } = [];

    public Dictionary<string, int> UnlessGroupAtLeast { get; init; } = [];
}

public sealed record FitThresholds
{
    public int Strong { get; init; } = 6;

    public int Maybe { get; init; } = 3;

    /// <summary>Scores below this go to <see cref="RoleScoringRules.BelowLevelChip"/>.</summary>
    public int Weak { get; init; }
}

/// <summary>Stated pay in alerts, compared with the owner's floor. Rates convert to USD; nothing is estimated without a stated range.</summary>
public sealed record PayRules
{
    public decimal? MonthlyFloorUsd { get; init; }

    public decimal HoursPerMonth { get; init; } = 160;

    public Dictionary<string, decimal> UsdPerUnit { get; init; } = new() { ["USD"] = 1 };

    public decimal MaxPlausibleMonthlyUsd { get; init; } = 60000;
}
