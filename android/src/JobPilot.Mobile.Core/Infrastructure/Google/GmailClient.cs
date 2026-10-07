using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JobPilot.Domain.Mail;
using JobPilot.Mobile.Core.Infrastructure.Text;

namespace JobPilot.Mobile.Core.Infrastructure.Google;

public sealed record MailThreadSummary(string Id, string Subject, string From, DateTime? DateUtc, string Snippet, int Messages);

public sealed record MailMessage(string Id, string From, string To, string Subject, DateTime? DateUtc, string MessageIdHeader, string Body);

public sealed record MailThread(string Id, string Subject, IReadOnlyList<MailMessage> Messages);

/// <param name="Truncated">Gmail has more results than <c>max</c>.</param>
public sealed record ThreadIdPage(IReadOnlyList<string> Ids, bool Truncated);

/// <param name="To">Recipient; may be empty (the owner completes it in Gmail).</param>
/// <param name="ThreadId">Gmail thread to reply in, or null for a new conversation.</param>
/// <param name="InReplyTo">Message-ID header of the message being answered.</param>
public sealed record DraftRequest(string? To, string Subject, string Body, string? ThreadId, string? InReplyTo);

/// <summary>
/// Gmail REST (v1): search and read threads (gmail.readonly) and create drafts (gmail.compose). The app never sends:
/// drafts stay in Gmail until the owner sends them there.
/// </summary>
public sealed partial class GmailClient(HttpClient http, GoogleAuthService auth) : GoogleApiClient(http, auth)
{
    private const string Me = "users/me";

    public async Task<GoogleResult<string>> ProfileEmailAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync<Profile>(HttpMethod.Get, $"{Me}/profile", null, GoogleScopes.GmailReadonly, cancellationToken);
        return result.Value is { } profile ? GoogleResult<string>.Ok(profile.EmailAddress) : GoogleResult<string>.Fail(result.Error!);
    }

    /// <param name="query">Gmail search syntax, e.g. <c>"Contoso" OR from:contoso.com</c>.</param>
    public async Task<GoogleResult<IReadOnlyList<MailThreadSummary>>> SearchAsync(string query, int max, CancellationToken cancellationToken)
    {
        var list = await SendAsync<ThreadList>(HttpMethod.Get, $"{Me}/threads?q={Uri.EscapeDataString(query)}&maxResults={max}", null, GoogleScopes.GmailReadonly, cancellationToken);
        if (list.Value is not { } threads)
        {
            return GoogleResult<IReadOnlyList<MailThreadSummary>>.Fail(list.Error!);
        }

        var summaries = new List<MailThreadSummary>();
        foreach (var thread in threads.Threads ?? [])
        {
            var detail = await SendAsync<GmailThread>(HttpMethod.Get,
                $"{Me}/threads/{Uri.EscapeDataString(thread.Id)}?format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=Date",
                null, GoogleScopes.GmailReadonly, cancellationToken);
            if (detail.Value is not { } full)
            {
                return GoogleResult<IReadOnlyList<MailThreadSummary>>.Fail(detail.Error!);
            }

            var first = full.Messages?.FirstOrDefault();
            var last = full.Messages?.LastOrDefault();
            summaries.Add(new MailThreadSummary(full.Id, Header(first, "Subject") ?? "(sin asunto)", Header(last, "From") ?? string.Empty,
                Date(last), HtmlText.ToPlainText(last?.Snippet ?? thread.Snippet ?? string.Empty), full.Messages?.Count ?? 0));
        }

        return GoogleResult<IReadOnlyList<MailThreadSummary>>.Ok(summaries);
    }

    public async Task<GoogleResult<MailThread>> GetThreadAsync(string threadId, CancellationToken cancellationToken)
    {
        var result = await SendAsync<GmailThread>(HttpMethod.Get, $"{Me}/threads/{Uri.EscapeDataString(threadId)}?format=full", null, GoogleScopes.GmailReadonly, cancellationToken);
        if (result.Value is not { } thread)
        {
            return GoogleResult<MailThread>.Fail(result.Error!);
        }

        var messages = (thread.Messages ?? []).Select(m => new MailMessage(
            m.Id,
            Header(m, "From") ?? string.Empty,
            Header(m, "To") ?? string.Empty,
            Header(m, "Subject") ?? string.Empty,
            Date(m),
            Header(m, "Message-ID") ?? Header(m, "Message-Id") ?? string.Empty,
            BodyText(m.Payload))).ToList();
        return GoogleResult<MailThread>.Ok(new MailThread(thread.Id, messages.FirstOrDefault()?.Subject ?? "(sin asunto)", messages));
    }

    /// <summary>Thread ids matching a query, newest first (spam and trash excluded).</summary>
    public async Task<GoogleResult<ThreadIdPage>> ListThreadIdsAsync(string query, int max, CancellationToken cancellationToken)
    {
        var list = await SendAsync<ThreadList>(HttpMethod.Get, $"{Me}/threads?q={Uri.EscapeDataString(query)}&maxResults={max}&includeSpamTrash=false",
            null, GoogleScopes.GmailReadonly, cancellationToken);
        return list.Value is { } threads
            ? GoogleResult<ThreadIdPage>.Ok(new ThreadIdPage((threads.Threads ?? []).Select(t => t.Id).ToList(), threads.NextPageToken is not null))
            : GoogleResult<ThreadIdPage>.Fail(list.Error!);
    }

    /// <summary>
    /// A thread with every message's headers, text and HTML parts and calendar invites, for the mail rules. Read in memory
    /// only: callers store facts (ids, sender, subject, dates), never bodies.
    /// </summary>
    /// <param name="ownEmail">The owner's address, to recognise their own messages besides Gmail's SENT label.</param>
    public async Task<GoogleResult<MailThreadInfo>> GetFullThreadAsync(string threadId, string? ownEmail, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        var result = await SendAsync<GmailThread>(HttpMethod.Get, $"{Me}/threads/{Uri.EscapeDataString(threadId)}?format=full", null, GoogleScopes.GmailReadonly, cancellationToken);
        if (result.Value is not { } thread)
        {
            return GoogleResult<MailThreadInfo>.Fail(result.Error!);
        }

        var messages = new List<MailMessageInfo>();
        foreach (var message in (thread.Messages ?? []).OrderBy(m => Date(m) ?? DateTime.MinValue))
        {
            var invites = new List<MailInvite>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in Flatten(message.Payload).Where(IsCalendarPart))
            {
                var data = part.Body?.Data;
                if (data is null && part.Body?.AttachmentId is { } attachmentId)
                {
                    var attachment = await SendAsync<PartBody>(HttpMethod.Get,
                        $"{Me}/messages/{Uri.EscapeDataString(message.Id)}/attachments/{Uri.EscapeDataString(attachmentId)}", null, GoogleScopes.GmailReadonly, cancellationToken);
                    data = attachment.Value?.Data;
                }

                if (data is null)
                {
                    continue;
                }

                // Gmail-style invites carry the same VEVENT twice: inline text/calendar and invite.ics.
                foreach (var invite in IcsInviteParser.Parse(Decode(part, data), ownEmail, zone))
                {
                    if (seen.Add($"{invite.Method}|{invite.Uid}|{invite.Sequence}|{invite.Start}"))
                    {
                        invites.Add(invite);
                    }
                }
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in message.Payload?.Headers ?? [])
            {
                headers.TryAdd(header.Name, header.Value);
            }

            var from = ParseAddress(Header(message, "From") ?? string.Empty);
            var text = BodyPart(message.Payload, "text/plain");
            var html = BodyPart(message.Payload, "text/html");
            messages.Add(new MailMessageInfo(
                message.Id,
                Date(message) is { } date ? new DateTimeOffset(date, TimeSpan.Zero) : DateTimeOffset.MinValue,
                from,
                Header(message, "Subject") ?? string.Empty,
                html,
                text,
                headers,
                invites,
                (message.LabelIds?.Contains("SENT") ?? false) || (ownEmail is not null && string.Equals(from.Address, ownEmail, StringComparison.OrdinalIgnoreCase)))
            {
                BodyText = text ?? (html is null ? string.Empty : HtmlText.ToPlainText(html)),
            });
        }

        return messages.Count == 0
            ? GoogleResult<MailThreadInfo>.Fail("El hilo no tiene mensajes.")
            : GoogleResult<MailThreadInfo>.Ok(new MailThreadInfo(thread.Id, messages));
    }

    /// <summary>"Ana Pérez" &lt;ana@example.com&gt; or a bare address.</summary>
    public static MailPerson ParseAddress(string value)
    {
        var match = NamedAddressRegex().Match(value);
        return match.Success
            ? new MailPerson(match.Groups["name"].Value.Trim().Trim('"').Trim(), match.Groups["address"].Value.Trim())
            : new MailPerson(string.Empty, value.Trim().Trim('<', '>'));
    }

    /// <summary>Creates a draft in the owner's Gmail. Returns the draft id. Never sends.</summary>
    public async Task<GoogleResult<string>> CreateDraftAsync(DraftRequest draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var message = new Dictionary<string, object> { ["raw"] = ToBase64Url(Encoding.UTF8.GetBytes(BuildMime(draft))) };
        if (!string.IsNullOrWhiteSpace(draft.ThreadId))
        {
            message["threadId"] = draft.ThreadId;
        }

        var result = await SendAsync<DraftResponse>(HttpMethod.Post, $"{Me}/drafts", new Dictionary<string, object> { ["message"] = message }, GoogleScopes.GmailCompose, cancellationToken);
        return result.Value is { } created ? GoogleResult<string>.Ok(created.Id) : GoogleResult<string>.Fail(result.Error!);
    }

    /// <summary>RFC 2822 message: UTF-8 body in base64, encoded subject, reply headers; CR/LF removed from header values.</summary>
    public static string BuildMime(DraftRequest draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(draft.To))
        {
            builder.Append("To: ").Append(HeaderValue(draft.To)).Append("\r\n");
        }

        builder.Append("Subject: =?UTF-8?B?").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(HeaderValue(draft.Subject)))).Append("?=\r\n");
        if (!string.IsNullOrWhiteSpace(draft.InReplyTo))
        {
            var id = HeaderValue(draft.InReplyTo);
            builder.Append("In-Reply-To: ").Append(id).Append("\r\n").Append("References: ").Append(id).Append("\r\n");
        }

        builder.Append("MIME-Version: 1.0\r\n");
        builder.Append("Content-Type: text/plain; charset=\"UTF-8\"\r\n");
        builder.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(draft.Body.ReplaceLineEndings("\r\n")));
        for (var i = 0; i < body.Length; i += 76)
        {
            builder.Append(body.AsSpan(i, Math.Min(76, body.Length - i))).Append("\r\n");
        }

        return builder.ToString();
    }

    public static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }

    /// <summary>Plain text of a message: the text/plain part, else the HTML part converted to text (never rendered).</summary>
    internal static string BodyText(MessagePart? part)
    {
        if (part is null)
        {
            return string.Empty;
        }

        var parts = Flatten(part).ToList();
        var plain = parts.FirstOrDefault(p => p.MimeType == "text/plain" && p.Body?.Data is not null);
        if (plain is not null)
        {
            return Encoding.UTF8.GetString(FromBase64Url(plain.Body!.Data!)).Trim();
        }

        var html = parts.FirstOrDefault(p => p.MimeType == "text/html" && p.Body?.Data is not null);
        return html is null ? string.Empty : HtmlText.ToPlainText(Encoding.UTF8.GetString(FromBase64Url(html.Body!.Data!)));
    }

    private static string? BodyPart(MessagePart? payload, string mimeType)
    {
        var part = Flatten(payload).FirstOrDefault(p => p.MimeType == mimeType && p.Body?.Data is not null && string.IsNullOrEmpty(p.Filename));
        return part is null ? null : Decode(part, part.Body!.Data!);
    }

    /// <summary>Part bytes in the charset its Content-Type names (UTF-8 when absent or unknown).</summary>
    private static string Decode(MessagePart part, string data)
    {
        var bytes = FromBase64Url(data);
        var contentType = part.Headers?.FirstOrDefault(h => string.Equals(h.Name, "Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
        var charset = CharsetRegex().Match(contentType);
        if (charset.Success)
        {
            try
            {
                return Encoding.GetEncoding(charset.Groups[1].Value).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // Unknown charset name: fall back to UTF-8.
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsCalendarPart(MessagePart part) =>
        part.MimeType is "text/calendar" or "application/ics"
        || (part.Filename?.EndsWith(".ics", StringComparison.OrdinalIgnoreCase) ?? false);

    [GeneratedRegex(@"charset\s*=\s*""?([\w.:-]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex CharsetRegex();

    [GeneratedRegex(@"^\s*(?<name>.*?)\s*<(?<address>[^<>]+)>\s*$")]
    private static partial Regex NamedAddressRegex();

    private static IEnumerable<MessagePart> Flatten(MessagePart? part)
    {
        if (part is null)
        {
            yield break;
        }

        yield return part;
        foreach (var child in part.Parts ?? [])
        {
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }

    private static string HeaderValue(string value) => value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

    private static string? Header(GmailMessage? message, string name) =>
        message?.Payload?.Headers?.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static DateTime? Date(GmailMessage? message) =>
        message?.InternalDate is { } ms && long.TryParse(ms, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
            : null;

    // Wire shapes (Gmail v1 discovery document).
    private sealed record Profile(string EmailAddress);

    private sealed record ThreadList(List<ThreadRef>? Threads, string? NextPageToken);

    private sealed record ThreadRef(string Id, string? Snippet);

    internal sealed record GmailThread(string Id, List<GmailMessage>? Messages);

    internal sealed record GmailMessage(string Id, string? Snippet, string? InternalDate, MessagePart? Payload, List<string>? LabelIds = null);

    internal sealed record MessagePart(string? MimeType, List<HeaderEntry>? Headers, PartBody? Body, List<MessagePart>? Parts, string? Filename = null);

    internal sealed record HeaderEntry(string Name, string Value);

    internal sealed record PartBody(string? Data, string? AttachmentId = null);

    private sealed record DraftResponse(string Id);
}
