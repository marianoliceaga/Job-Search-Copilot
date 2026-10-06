using JobPilot.Domain.Mail;

using static JobPilot.Domain.Tests.Mail.MailTestData;

namespace JobPilot.Domain.Tests.Mail;

public class MailClassifierTests
{
    private static readonly Dictionary<string, string> AlertHeaders = new() { ["X-LinkedIn-Template"] = "email_job_alert_digest_01" };

    private static AlertParseResult ParseAlert(MailMessageInfo message) =>
        message.Subject.Contains("empty", StringComparison.Ordinal)
            ? new AlertParseResult([], UsedTextFallback: true)
            : new AlertParseResult([Listing(message.Id, "Senior .NET Engineer")], UsedTextFallback: false);

    private static MailClassification Classify(params MailThreadInfo[] threads) =>
        MailClassifier.Compile(Rules()).Classify(threads, [new MailFetchFailure("t-broken", "500 backend error")], ParseAlert, Now, Zone);

    [Fact]
    public void Every_thread_lands_in_exactly_one_bucket()
    {
        var result = Classify(
            Thread("t-alert", Message("m1", "jobalerts-noreply@linkedin.com", "Senior .NET: 3 new jobs", headers: AlertHeaders)),
            Thread("t-recruiter", Message("m2", "ana@example.com", "Opportunity at Contoso", "We have a role for you", name: "Ana Pérez")),
            Thread("t-news", Message("m3", "news@example.org", "Weekly digest", headers: new() { ["List-Unsubscribe"] = "<mailto:x>" })),
            Thread("t-mine", Message("m4", "ana@example.com", "Interview"), Message("m5", "me@example.com", "Re: Interview", fromMe: true)));

        Assert.Equal(new MailBucketCounts(Waiting: 1, Roles: 1, Filtered: 3, ThreadsRead: 5), result.Counts);
        Assert.True(result.Counts.Balanced);
        Assert.Equal("recruiter-outreach", Assert.Single(result.Waiting).RuleId);
        Assert.Equal(["Escribiste último", "Newsletters", "No se pudieron leer"], result.Filtered.Select(c => c.Label).Order(StringComparer.Ordinal));
        Assert.Single(result.Roles.Shown);
    }

    [Fact]
    public void A_waiting_card_has_the_sender_a_factual_timeline_and_one_next_action()
    {
        var result = Classify(Thread("t1",
            Message("m1", "ana@example.com", "Interview availability", "Could you share your availability?", Now.AddDays(-1), name: "Ana Pérez via LinkedIn"),
            Message("m2", "me@example.com", "Re: Interview availability", received: Now.AddHours(-20), fromMe: true),
            Message("m3", "ana@example.com", "Re: Interview availability", "Thursday works", Now.AddHours(-2), name: "Ana Pérez via LinkedIn")));

        var card = Assert.Single(result.Waiting);
        Assert.Equal("interview-scheduling", card.RuleId);
        Assert.Equal("Ana Pérez", card.SenderName);
        Assert.Equal("Mandale tu disponibilidad a Ana Pérez.", card.NextAction);
        Assert.Equal("m3", card.LastMessageId);
        Assert.Equal(3, card.Timeline.Split(" · ").Length);
        Assert.StartsWith("Ana Pérez via LinkedIn escribió", card.Timeline, StringComparison.Ordinal);
        Assert.Contains("Respondiste", card.Timeline, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkedIn_inmails_are_recognised_by_their_template_header_even_as_bulk_mail()
    {
        var headers = new Dictionary<string, string> { ["X-LinkedIn-Template"] = "email_hire_inmail_initial_01", ["List-Unsubscribe"] = "<x>" };

        var result = Classify(Thread("t1", Message("m1", "inmail-noreply@linkedin.com", "Ana sent you a message", headers: headers, name: "Ana Pérez")));

        Assert.Equal("linkedin-inmail", Assert.Single(result.Waiting).RuleId);
    }

    [Fact]
    public void Automated_senders_are_never_waiting_on_you()
    {
        var result = Classify(Thread("t1", Message("m1", "no-reply@greenhouse.io", "Your interview availability", "Pick a time for your interview")));

        Assert.Empty(result.Waiting);
        Assert.Contains(result.Filtered, c => c.Id == "applications");
    }

    [Fact]
    public void Future_invites_the_owner_has_not_answered_ask_for_an_rsvp()
    {
        var invite = new MailInvite("REQUEST", "uid-1", 0, "Technical interview", Now.AddDays(2), Now.AddDays(2).AddHours(1), false, null, "NEEDS-ACTION");
        var past = invite with { Uid = "uid-2", Start = Now.AddDays(-1), End = Now.AddDays(-1).AddHours(1) };

        var result = Classify(
            Thread("t1", Message("m1", "calendar@example.com", "Invitation: Technical interview", invites: [invite])),
            Thread("t2", Message("m2", "calendar@example.com", "Invitation: Old interview", invites: [past])));

        var card = Assert.Single(result.Waiting);
        Assert.Equal(("rsvp", "t1", "Invitación"), (card.RuleId, card.ThreadId, card.RailNote));
        Assert.Equal(invite.Start, card.RailTime);
        Assert.StartsWith("Respondé a “Technical interview”", card.NextAction, StringComparison.Ordinal);
    }

    [Fact]
    public void Alerts_the_parser_cannot_read_are_counted_not_lost()
    {
        var result = Classify(Thread("t1", Message("m1", "jobalerts-noreply@linkedin.com", "empty alert", headers: AlertHeaders)));

        Assert.Contains(result.Filtered, c => c.Id == MailClassifier.UnparsedAlertChipId && c.Count == 1);
        Assert.Empty(result.Roles.Shown);
        Assert.True(result.Counts.Balanced);
    }

    [Fact]
    public void Repeated_rule_ids_are_rejected()
    {
        var rules = Rules() with { FilterCategories = [new() { Id = "rsvp", Chip = "x" }] };

        Assert.Throws<MailRulesException>(() => MailClassifier.Compile(rules));
    }
}
