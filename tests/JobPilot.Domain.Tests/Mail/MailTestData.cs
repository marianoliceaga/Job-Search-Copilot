using JobPilot.Domain.Mail;

namespace JobPilot.Domain.Tests.Mail;

/// <summary>Small, fictional rules and messages for the mail tests.</summary>
internal static class MailTestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    public static MailRules Rules() => new()
    {
        Sets = new()
        {
            ["hiringSystems"] = ["greenhouse.io", "lever.co"],
            ["interviewTerms"] = ["interview", "entrevista", "availability"],
            ["recruiterTerms"] = ["opportunity", "oportunidad", "role"],
            ["dotnet"] = [@"re:(?<![\p{L}\p{N}])\.net\b", "dotnet", @"re:(?<![\p{L}\p{N}])c#"],
            ["otherStack"] = ["java", "python"],
            ["levelSenior"] = ["senior", "sr"],
            ["levelJunior"] = ["junior", "ssr"],
            ["remote"] = ["remote", "remoto"],
            ["latam"] = ["latam", "argentina"],
            ["home"] = ["mendoza"],
            ["usOnly"] = ["united states", @"re:,\s?(ca|ny|tx)$"],
        },
        AutomatedSenders = [@"re:^(no-?reply|notifications?)[@+._-]", "$hiringSystems"],
        LinkedIn = new() { AlertSenders = ["jobalerts-noreply@linkedin.com"], TemplateMatches = ["re:job_alert"] },
        WaitingRules =
        [
            new() { Id = "rsvp", Kind = WaitingRuleKind.InviteNeedsReply, Pill = "RSVP", NextAction = "Respondé a “{inviteTitle}”, {inviteWhen}." },
            new()
            {
                Id = "linkedin-inmail", Pill = "Responder",
                Match = new() { From = ["linkedin.com"], Headers = new() { ["X-LinkedIn-Template"] = ["re:inmail_initial"] } },
                NextAction = "Respondele a {senderName} en LinkedIn.",
            },
            new()
            {
                Id = "interview-scheduling", Pill = "Responder",
                Match = new() { HumanSender = true, HasInvite = false, Text = ["$interviewTerms"] },
                NextAction = "Mandale tu disponibilidad a {senderName}.",
            },
            new()
            {
                Id = "recruiter-outreach", Pill = "Responder",
                Match = new() { HumanSender = true, HasInvite = false, Text = ["$recruiterTerms"] },
                NextAction = "Respondele a {senderName}: te interesa o no.",
            },
        ],
        FilterCategories =
        [
            new() { Id = "you-wrote-last", Chip = "Escribiste último", Match = new() { LastFromMe = true } },
            new() { Id = "applications", Chip = "Postulaciones sin acción", Match = new() { From = ["$hiringSystems"] } },
            new() { Id = "newsletters", Chip = "Newsletters", Match = new() { Bulk = true } },
            new() { Id = "other", Chip = "Otros", Match = new() },
        ],
        Scoring = new()
        {
            Rules =
            [
                new() { Id = "dotnet", Why = "+.NET en el título", Group = "stack", Weight = 3, When = [new() { Field = "title", Any = ["$dotnet"] }] },
                new() { Id = "other-stack", Why = "-otro stack", Group = "stack", Weight = -3, When = [new() { Field = "title", Any = ["$otherStack"], None = ["$dotnet"] }] },
                new() { Id = "senior", Why = "+senior", Group = "level", Weight = 1, When = [new() { Field = "title", Any = ["$levelSenior"], None = ["$levelJunior"] }] },
                new() { Id = "junior", Why = "-por debajo de senior", Group = "level", Weight = -4, When = [new() { Field = "title", Any = ["$levelJunior"] }] },
                new()
                {
                    Id = "remote-latam", Why = "+remoto LatAm", Group = "location", Weight = 3,
                    When = [new() { Field = "workplace,title", Any = ["$remote"] }, new() { Field = "title,location", Any = ["$latam"] }],
                },
                new() { Id = "pay-above", Why = "+pago hasta {payMonthly}/mes", Group = "pay", Weight = 2, When = [new() { Pay = "atOrAboveFloor" }] },
                new() { Id = "pay-below", Why = "-pago tope {payMonthly}/mes", Group = "pay", Weight = -3, When = [new() { Pay = "belowFloor" }] },
            ],
            Filters =
            [
                new()
                {
                    Id = "onsite-away", Chip = "Presencial fuera de tu ciudad",
                    When = [new() { Field = "workplace", Any = ["onsite"] }, new() { Field = "location", None = ["$home"] }],
                    UnlessGroupAtLeast = new() { ["stack"] = 3 },
                },
                new()
                {
                    Id = "remote-us-only", Chip = "Remoto, solo EE. UU.",
                    When = [new() { Field = "workplace,title", Any = ["$remote"] }, new() { Field = "location", Any = ["$usOnly"] }, new() { Field = "title,location", None = ["$latam"] }],
                },
            ],
            Thresholds = new() { Strong = 6, Maybe = 3, Weak = 0 },
            Pay = new() { MonthlyFloorUsd = 2500, UsdPerUnit = new() { ["USD"] = 1, ["EUR"] = 1.1m } },
        },
    };

    public static MailMessageInfo Message(
        string id,
        string from,
        string subject,
        string body = "",
        DateTimeOffset? received = null,
        bool fromMe = false,
        Dictionary<string, string>? headers = null,
        IReadOnlyList<MailInvite>? invites = null,
        string? name = null) =>
        new(id, received ?? Now.AddHours(-2), new MailPerson(name ?? string.Empty, from), subject, null, body,
            headers ?? [], invites ?? [], fromMe);

    public static MailThreadInfo Thread(string id, params MailMessageInfo[] messages) => new(id, messages);

    public static AlertListing Listing(string jobId, string title, string company = "Contoso", string location = "Argentina", string workplace = "remote", AlertPay? pay = null) =>
        new(jobId, title, company, location, workplace, pay, [], "Senior .NET", "t-alert", Now.AddHours(-3));
}
