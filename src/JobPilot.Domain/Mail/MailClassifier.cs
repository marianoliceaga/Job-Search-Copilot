using System.Globalization;
using System.Text.RegularExpressions;

namespace JobPilot.Domain.Mail;

public enum MailBucket
{
    Waiting,
    Roles,
    Filtered,
}

/// <summary>A "Te esperan" card: who wrote, a factual timeline and one next action. Facts only, no mail body.</summary>
public sealed record WaitingItem(
    string ThreadId,
    string LastMessageId,
    string RuleId,
    string Pill,
    MailTone Tone,
    string Subject,
    string SenderName,
    string SenderAddress,
    string Timeline,
    string NextAction,
    DateTimeOffset LastReceived,
    DateTimeOffset RailTime,
    string RailNote);

/// <summary>A count in "Filtrados", with a few example senders so nothing disappears unseen.</summary>
public sealed record FilterChip(string Id, string Label, int Count, IReadOnlyList<string> Examples);

public sealed record ThreadAssignment(string ThreadId, MailBucket Bucket, string RuleId);

public sealed record MailBucketCounts(int Waiting, int Roles, int Filtered, int ThreadsRead)
{
    public int Total => Waiting + Roles + Filtered;

    /// <summary>The invariant: every thread read lands in exactly one bucket.</summary>
    public bool Balanced => Total == ThreadsRead;
}

public sealed record MailClassification(
    IReadOnlyList<WaitingItem> Waiting,
    RolesResult Roles,
    IReadOnlyList<FilterChip> Filtered,
    IReadOnlyList<FilterChip> RoleFilters,
    IReadOnlyList<ThreadAssignment> Assignments,
    int ThreadsRead,
    IReadOnlyList<string> Warnings)
{
    public MailBucketCounts Counts => new(
        Assignments.Count(a => a.Bucket == MailBucket.Waiting),
        Assignments.Count(a => a.Bucket == MailBucket.Roles),
        Assignments.Count(a => a.Bucket == MailBucket.Filtered),
        ThreadsRead);
}

/// <summary>
/// Sorts job-search mail. Every thread read ends in exactly one bucket, tried in this order:
/// LinkedIn job alert → "Te esperan" → "Filtrados" (the last category catches all). Unreadable threads count as filtered.
/// Ported from JobSearchDesk without banks, bills or security notices (job search only).
/// </summary>
public sealed partial class MailClassifier
{
    public const string UnreadableChipId = "unreadable";
    public const string UnparsedAlertChipId = "linkedin-unparsed";
    public const string UnmatchedChipId = "unmatched";
    public const string AlertRuleId = "linkedin-alert";

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-AR");

    private readonly MailRules _rules;
    private readonly SenderSet _alertSenders;
    private readonly TermSet _alertTemplates;
    private readonly SenderSet _automated;
    private readonly List<(WaitingRule Rule, ThreadMatcher Matcher)> _waiting;
    private readonly List<(FilterCategory Category, ThreadMatcher Matcher)> _categories;

    private MailClassifier(MailRules rules, SenderSet alertSenders, TermSet alertTemplates, SenderSet automated,
        List<(WaitingRule, ThreadMatcher)> waiting, List<(FilterCategory, ThreadMatcher)> categories, RoleScorer scorer)
    {
        _rules = rules;
        _alertSenders = alertSenders;
        _alertTemplates = alertTemplates;
        _automated = automated;
        _waiting = waiting;
        _categories = categories;
        Scorer = scorer;
    }

    public RoleScorer Scorer { get; }

    /// <summary>Compiles every rule; an unknown set or a bad pattern fails here, before any mail is read.</summary>
    public static MailClassifier Compile(MailRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var sets = new SetResolver(rules.Sets);
        var automated = SenderSet.Compile(sets.Expand(rules.AutomatedSenders, "automatedSenders"), "automatedSenders");
        var duplicate = rules.WaitingRules.Select(r => r.Id).Concat(rules.FilterCategories.Select(c => c.Id))
            .GroupBy(id => id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new MailRulesException($"El id \"{duplicate.Key}\" está repetido en waitingRules/filterCategories.");
        }

        return new MailClassifier(
            rules,
            SenderSet.Compile(sets.Expand(rules.LinkedIn.AlertSenders, "linkedIn.alertSenders"), "linkedIn.alertSenders"),
            TermSet.Compile(sets.Expand(rules.LinkedIn.TemplateMatches, "linkedIn.templateMatches"), "linkedIn.templateMatches"),
            automated,
            rules.WaitingRules.Select((r, i) => (r, ThreadMatcher.Compile(r.Match, sets, $"waitingRules[{i}] ({r.Id})"))).ToList(),
            rules.FilterCategories.Select((c, i) => (c, ThreadMatcher.Compile(c.Match, sets, $"filterCategories[{i}] ({c.Id})"))).ToList(),
            RoleScorer.Compile(rules.Scoring, sets));
    }

    public MailClassification Classify(
        IReadOnlyList<MailThreadInfo> threads,
        IReadOnlyList<MailFetchFailure> failures,
        Func<MailMessageInfo, string, AlertParseResult> parseAlert,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(threads);
        ArgumentNullException.ThrowIfNull(failures);
        ArgumentNullException.ThrowIfNull(parseAlert);
        var warnings = new List<string>();
        if (_rules.FilterCategories.Count == 0)
        {
            warnings.Add("Las reglas de correo no tienen filterCategories: todo lo que no coincida queda como \"Sin regla\".");
        }

        var assignments = new List<ThreadAssignment>();
        var waiting = new List<WaitingItem>();
        var chips = new Dictionary<string, (string Label, List<string> Senders)>(StringComparer.Ordinal);
        var listings = new List<AlertListing>();
        var fallbacks = 0;

        void Chip(string id, string label, string threadId, string sender)
        {
            if (!chips.TryGetValue(id, out var chip))
            {
                chips[id] = chip = (label, []);
            }

            chip.Senders.Add(sender);
            assignments.Add(new ThreadAssignment(threadId, MailBucket.Filtered, id));
        }

        foreach (var thread in threads)
        {
            var facts = new ThreadFacts(thread);
            if (IsJobAlert(facts))
            {
                var parsed = thread.Messages.Select(m => parseAlert(m, thread.Id)).ToList();
                var found = parsed.SelectMany(p => p.Listings).ToList();
                if (found.Count == 0)
                {
                    Chip(UnparsedAlertChipId, "Alertas de LinkedIn que no se pudieron leer", thread.Id, facts.Sender.Display);
                    continue;
                }

                fallbacks += parsed.Count(p => p.UsedTextFallback);
                listings.AddRange(found);
                assignments.Add(new ThreadAssignment(thread.Id, MailBucket.Roles, AlertRuleId));
                continue;
            }

            if (MatchWaiting(facts, now, zone) is { } item)
            {
                waiting.Add(item);
                assignments.Add(new ThreadAssignment(thread.Id, MailBucket.Waiting, item.RuleId));
                continue;
            }

            var category = _categories.FirstOrDefault(c => c.Matcher.Matches(facts, _automated));
            if (category.Category is not null)
            {
                Chip(category.Category.Id, category.Category.Chip, thread.Id, facts.Sender.Display);
            }
            else
            {
                Chip(UnmatchedChipId, "Sin regla", thread.Id, facts.Sender.Display);
            }
        }

        foreach (var failure in failures)
        {
            Chip(UnreadableChipId, "No se pudieron leer", failure.ThreadId, failure.Reason);
        }

        if (fallbacks > 0)
        {
            warnings.Add($"{fallbacks} alerta(s) de LinkedIn se leyeron de la versión en texto: el formato HTML puede haber cambiado y falta la modalidad y el pago de esos puestos.");
        }

        var roles = Scorer.Score(listings);
        var filtered = chips
            .Select(kv => new FilterChip(kv.Key, kv.Value.Label, kv.Value.Senders.Count, kv.Value.Senders.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList()))
            .OrderByDescending(c => c.Count).ThenBy(c => c.Label, StringComparer.Ordinal)
            .ToList();
        var roleFilters = roles.Filtered.GroupBy(r => r.FilteredBy!, StringComparer.Ordinal)
            .Select(g => new FilterChip("role-" + g.Key, g.Key, g.Count(), g.Select(r => $"{r.Best.Company}, {r.Best.Title}").Take(3).ToList()))
            .ToList();

        var result = new MailClassification(
            waiting.OrderBy(w => w.Tone).ThenBy(w => w.RailTime).ToList(),
            roles, filtered, roleFilters, assignments, threads.Count + failures.Count, warnings);
        if (!result.Counts.Balanced)
        {
            // Never present a partial picture: a thread that vanished would be a silent loss.
            throw new InvalidOperationException($"Mail buckets do not add up: {result.Counts.Total} assigned, {result.Counts.ThreadsRead} read.");
        }

        return result;
    }

    /// <summary>A factual account of the thread, oldest first: who wrote when, and what each invite version changed.</summary>
    public static string Timeline(MailThreadInfo thread, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var entries = new List<string>();
        var previous = new Dictionary<string, MailInvite>(StringComparer.Ordinal);
        foreach (var m in thread.Messages)
        {
            var at = Moment(m.Received, zone);
            if (m.IsFromMe)
            {
                entries.Add($"Respondiste {at}");
                continue;
            }

            if (m.Invites.Count == 0)
            {
                entries.Add($"{m.From.Display} escribió {at}");
                continue;
            }

            foreach (var invite in m.Invites)
            {
                previous.TryGetValue(invite.Uid, out var before);
                var when = invite.Start is { } s ? Span(s, invite.End, invite.AllDay, zone) : "un horario sin indicar";
                var entry = (invite.Method, before) switch
                {
                    ("CANCEL", _) => $"Cancelada {at}",
                    (_, null) => invite.Method == "PUBLISH" ? $"Detalles enviados {at} para {when}" : $"Invitación enviada {at} para {when}",
                    (_, { Method: "CANCEL" }) => $"Reprogramada {at}{(invite.Start != before.Start ? $" para {when}" : string.Empty)}",
                    _ when invite.Start != before.Start => $"Movida {at} a {when}",
                    _ => $"Actualizada {at}",
                };
                entries.Add(entry);
                previous[invite.Uid] = invite;
            }
        }

        const int maxEntries = 6;
        return entries.Count <= maxEntries ? string.Join(" · ", entries) : "… " + string.Join(" · ", entries.Skip(entries.Count - maxEntries));
    }

    public static string Moment(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString("ddd d MMM HH:mm", Spanish);

    public static string Span(DateTimeOffset start, DateTimeOffset? end, bool allDay, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(start, zone);
        if (allDay)
        {
            return local.ToString("ddd d MMM", Spanish) + " (todo el día)";
        }

        var text = local.ToString("ddd d MMM HH:mm", Spanish);
        return end is { } e ? $"{text}–{TimeZoneInfo.ConvertTime(e, zone):HH:mm}" : text;
    }

    private bool IsJobAlert(ThreadFacts facts) =>
        facts.Thread.Messages.All(m => _alertSenders.IsMatch(m.From.Address))
        && facts.Thread.Messages.Any(m => _alertTemplates.IsMatch(MailText.Normalize(m.Header(_rules.LinkedIn.TemplateHeader))));

    private WaitingItem? MatchWaiting(ThreadFacts facts, DateTimeOffset now, TimeZoneInfo zone)
    {
        // Only threads in which someone else spoke last can be waiting on the owner.
        if (facts.LastFromMe || facts.LastIncoming is null)
        {
            return null;
        }

        foreach (var (rule, matcher) in _waiting)
        {
            if (!matcher.Matches(facts, _automated))
            {
                continue;
            }

            var invite = rule.Kind switch
            {
                // Without reading the calendar, the e-mail copy decides: the owner can dismiss a card once answered.
                WaitingRuleKind.InviteNeedsReply => facts.CurrentInvites.FirstOrDefault(i =>
                    i.Method == "REQUEST" && i.Start > now && string.Equals(i.MyResponse, "NEEDS-ACTION", StringComparison.OrdinalIgnoreCase)),
                WaitingRuleKind.InviteUpcoming => facts.CurrentInvites.FirstOrDefault(i => i.Method != "CANCEL" && i.Start > now),
                _ => null,
            };
            if (rule.Kind != WaitingRuleKind.Match && invite is null)
            {
                continue;
            }

            var sender = facts.Sender;
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["subject"] = facts.Thread.Subject,
                ["sender"] = sender.Display,
                // LinkedIn names message senders "Ana Pérez via LinkedIn"; the person is enough.
                ["senderName"] = string.IsNullOrWhiteSpace(sender.Name) ? sender.Address : ViaSuffixRegex().Replace(sender.Name, string.Empty),
                ["senderAddress"] = sender.Address,
                ["received"] = Moment(facts.LastIncoming.Received, zone),
                ["inviteTitle"] = invite?.Summary ?? facts.Thread.Subject,
                ["inviteWhen"] = invite?.Start is { } start ? Span(start, invite.End, invite.AllDay, zone) : string.Empty,
            };

            return new WaitingItem(
                facts.Thread.Id,
                facts.Thread.Latest.Id,
                rule.Id,
                rule.Pill,
                rule.Tone,
                facts.Thread.Subject,
                values["senderName"],
                sender.Address,
                Timeline(facts.Thread, zone),
                MailTemplate.Fill(rule.NextAction, values),
                facts.LastIncoming.Received,
                invite?.Start ?? facts.LastIncoming.Received,
                invite is null ? "Recibido" : invite.Method == "PUBLISH" ? "Entrevista" : "Invitación");
        }

        return null;
    }

    [GeneratedRegex(@"\s+(via|vía)\s+\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex ViaSuffixRegex();

    /// <summary>What the rules look at for one thread, worked out once.</summary>
    internal sealed class ThreadFacts
    {
        private static readonly string[] BulkPrecedence = ["bulk", "list", "junk"];

        public ThreadFacts(MailThreadInfo thread)
        {
            Thread = thread;
            LastFromMe = thread.Latest.IsFromMe;
            LastIncoming = thread.Messages.LastOrDefault(m => !m.IsFromMe);
            var reference = LastIncoming ?? thread.Latest;
            Sender = reference.From;
            Bulk = reference.Header("List-Unsubscribe") is not null
                || reference.Header("List-Id") is not null
                || (reference.Header("Precedence") is { } p && BulkPrecedence.Contains(p.Trim().ToLowerInvariant()));
            NormalizedSubject = MailText.Normalize(thread.Subject);
            var body = reference.BodyText.Length > 4000 ? reference.BodyText[..4000] : reference.BodyText;
            NormalizedText = MailText.Normalize($"{thread.Subject} {reference.Subject} {body}");
            CurrentInvites = thread.Messages
                .SelectMany(m => m.Invites.Select(i => (Invite: i, m.Received)))
                .OrderBy(v => v.Received).ThenBy(v => v.Invite.Sequence)
                .GroupBy(v => v.Invite.Uid, StringComparer.Ordinal)
                .Select(g => g.Last().Invite)
                .ToList();
        }

        public MailThreadInfo Thread { get; }

        /// <summary>The latest message the owner did not write, or null if the owner wrote them all.</summary>
        public MailMessageInfo? LastIncoming { get; }

        public bool LastFromMe { get; }

        public MailPerson Sender { get; }

        public bool Bulk { get; }

        public string NormalizedSubject { get; }

        public string NormalizedText { get; }

        /// <summary>The newest version of each invite in the thread (by UID), including cancellations.</summary>
        public IReadOnlyList<MailInvite> CurrentInvites { get; }

        public bool HasInvite => CurrentInvites.Count > 0;
    }

    /// <summary>A compiled <see cref="ThreadMatch"/>.</summary>
    internal sealed class ThreadMatcher
    {
        private readonly ThreadMatch _match;
        private readonly SenderSet _from;
        private readonly SenderSet _notFrom;
        private readonly TermSet _subject;
        private readonly TermSet _text;
        private readonly TermSet _notText;
        private readonly List<(string Name, TermSet Terms)> _headers;

        private ThreadMatcher(ThreadMatch match, SenderSet from, SenderSet notFrom, TermSet subject, TermSet text, TermSet notText, List<(string, TermSet)> headers)
        {
            _match = match;
            _from = from;
            _notFrom = notFrom;
            _subject = subject;
            _text = text;
            _notText = notText;
            _headers = headers;
        }

        public static ThreadMatcher Compile(ThreadMatch match, SetResolver sets, string context) => new(
            match,
            SenderSet.Compile(sets.Expand(match.From, context), $"{context}.from"),
            SenderSet.Compile(sets.Expand(match.NotFrom, context), $"{context}.notFrom"),
            TermSet.Compile(sets.Expand(match.Subject, context), $"{context}.subject"),
            TermSet.Compile(sets.Expand(match.Text, context), $"{context}.text"),
            TermSet.Compile(sets.Expand(match.NotText, context), $"{context}.notText"),
            (match.Headers ?? []).Select(h => (h.Key, TermSet.Compile(sets.Expand(h.Value, context), $"{context}.headers.{h.Key}"))).ToList());

        public bool Matches(ThreadFacts facts, SenderSet automated)
        {
            var m = _match;
            var human = facts.LastIncoming is not null && !facts.Bulk && !automated.IsMatch(facts.Sender.Address);
            return (m.From is null || _from.IsMatch(facts.Sender.Address))
                && (m.NotFrom is null || !_notFrom.IsMatch(facts.Sender.Address))
                && (m.Subject is null || _subject.IsMatch(facts.NormalizedSubject))
                && (m.Text is null || _text.IsMatch(facts.NormalizedText))
                && (m.NotText is null || !_notText.IsMatch(facts.NormalizedText))
                && (m.HumanSender is not { } wantsHuman || wantsHuman == human)
                && (m.Bulk is not { } bulk || bulk == facts.Bulk)
                && (m.LastFromMe is not { } mine || mine == facts.LastFromMe)
                && (m.HasInvite is not { } invite || invite == facts.HasInvite)
                && _headers.All(h => (facts.LastIncoming ?? facts.Thread.Latest).Header(h.Name) is { } value && h.Terms.IsMatch(MailText.Normalize(value)));
        }
    }
}
