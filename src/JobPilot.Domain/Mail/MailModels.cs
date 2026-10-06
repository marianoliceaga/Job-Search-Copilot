namespace JobPilot.Domain.Mail;

public sealed record MailPerson(string Name, string Address)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? Address : Name;

    public string Domain => Address.Contains('@', StringComparison.Ordinal) ? Address[(Address.LastIndexOf('@') + 1)..].ToLowerInvariant() : string.Empty;
}

/// <summary>One VEVENT from an .ics invite in a message. <see cref="MyResponse"/> is the owner's PARTSTAT, if listed.</summary>
public sealed record MailInvite(
    string Method,
    string Uid,
    int Sequence,
    string? Summary,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    bool AllDay,
    MailPerson? Organizer,
    string? MyResponse);

/// <summary>A message as read from Gmail, in memory only: bodies are never stored.</summary>
public sealed record MailMessageInfo(
    string Id,
    DateTimeOffset Received,
    MailPerson From,
    string Subject,
    string? HtmlBody,
    string? TextBody,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyList<MailInvite> Invites,
    bool IsFromMe)
{
    /// <summary>Plain text used for matching: the text part, else the HTML part without markup (set by the reader).</summary>
    public string BodyText { get; init; } = TextBody ?? string.Empty;

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

public sealed record MailThreadInfo(string Id, IReadOnlyList<MailMessageInfo> Messages)
{
    public MailMessageInfo First => Messages[0];

    public MailMessageInfo Latest => Messages[^1];

    public string Subject => string.IsNullOrWhiteSpace(First.Subject) ? "(sin asunto)" : First.Subject;
}

/// <summary>A thread a query returned but that could not be read. Still counted, so nothing vanishes silently.</summary>
public sealed record MailFetchFailure(string ThreadId, string Reason);

/// <summary>Pay as stated in an alert ("$80K–$100K/yr"). Period: year | month | hour.</summary>
public sealed record AlertPay(decimal Low, decimal High, string Currency, string Period, string Text);

/// <summary>One role as it appeared in one LinkedIn alert e-mail. Workplace: remote | hybrid | onsite | empty when unstated.</summary>
public sealed record AlertListing(
    string JobId,
    string Title,
    string Company,
    string Location,
    string Workplace,
    AlertPay? Pay,
    IReadOnlyList<string> Flags,
    string AlertName,
    string ThreadId,
    DateTimeOffset Received)
{
    /// <summary>The canonical LinkedIn job link (the e-mail's tracking parameters are dropped).</summary>
    public string Url => $"https://www.linkedin.com/jobs/view/{JobId}/";
}

/// <summary>What a LinkedIn alert parser returns for one message.</summary>
public sealed record AlertParseResult(IReadOnlyList<AlertListing> Listings, bool UsedTextFallback);
