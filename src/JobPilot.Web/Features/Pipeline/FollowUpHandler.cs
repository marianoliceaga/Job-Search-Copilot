using System.ComponentModel;
using System.Globalization;
using System.Text;
using JobPilot.Domain.Pipeline;
using JobPilot.Domain.Prompts;
using JobPilot.Domain.Tailoring;
using JobPilot.Web.Features.Jobs.Analysis;
using JobPilot.Web.Features.Tailoring;
using JobPilot.Web.Infrastructure.Llm;

namespace JobPilot.Web.Features.Pipeline;

public sealed record FollowUpParagraphResponse(
    [property: Description("The paragraph, in English.")] string Text,
    [property: Description("References of the CV elements the paragraph relies on (e.g. \"E1.A2\"). Empty when it says nothing about the candidate's experience.")] List<string> Sources);

public sealed record FollowUpDraftResponse(
    [property: Description("Subject line in English, at most 80 characters.")] string Subject,
    [property: Description("Body paragraphs (2-3), without greeting or sign-off.")] List<FollowUpParagraphResponse> Paragraphs,
    [property: Description("In Spanish (Rioplatense): what the message does and what the candidate should check before sending, in 1-2 sentences.")] string Notes);

/// <param name="AllowedText">Facts, instructions, notes and posting: numbers in the draft must come from here or the CV.</param>
public sealed record PreparedFollowUp(Guid ApplicationId, TailoringContext Context, string Greeting, string SignOff, string AllowedText, RenderedPrompt Prompt);

public sealed record FollowUpDraft(string Subject, string Body, string? Notes, IReadOnlyList<string> Warnings)
{
    public string FullText => $"Subject: {Subject}\n\n{Body}";
}

public sealed record FollowUpResult(LlmError? Error, FollowUpDraft? Draft);

/// <summary>
/// Drafts a follow-up e-mail for an application. The draft is recorded in the timeline and shown to the owner, who edits,
/// copies and sends it themselves: the app never sends messages.
/// </summary>
public sealed class FollowUpHandler(
    ApplicationStore store,
    TailoringContextFactory contexts,
    PromptLibrary prompts,
    ILlmClient llm,
    TimeProvider time)
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <param name="contactId">Recipient among the application's contacts; null for "the hiring team".</param>
    /// <param name="instructions">What the owner wants to say (optional).</param>
    public async Task<(PreparedFollowUp? Prepared, string? Error)> PrepareAsync(Guid applicationId, long? contactId, string? instructions, CancellationToken cancellationToken)
    {
        var detail = await store.GetAsync(applicationId, cancellationToken);
        if (detail is null)
        {
            return (null, "La postulación no existe.");
        }

        var (context, error) = await contexts.CreateAsync(detail.Posting.Id, cvVersion: null, cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        var contact = detail.Application.Contacts.FirstOrDefault(c => c.Id == contactId);
        var facts = Facts(detail, contact, time.GetUtcNow().UtcDateTime);
        var notes = string.Join("\n", detail.Application.Timeline().Where(e => e.Kind is ApplicationEventKind.Note or ApplicationEventKind.ResponseReceived && e.Text is not null)
            .Select(e => $"- {e.OccurredAtUtc.ToString("d MMMM yyyy", English)}: {e.Text}"));
        var posting = JobAnalysisHandler.PostingText(detail.Posting);
        var ownerInstructions = string.IsNullOrWhiteSpace(instructions) ? "(none)" : instructions.Trim();

        var prompt = prompts.Get(LlmUseCases.FollowUpEmail).Render(new Dictionary<string, string>
        {
            ["facts"] = facts,
            ["instructions"] = ownerInstructions,
            ["cv"] = context.CvWithReferences(),
            ["notes"] = notes.Length == 0 ? "(none)" : notes,
            ["posting"] = posting,
        });

        var greeting = contact is null ? "Hello," : $"Hi {contact.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]},";
        var name = context.Cv.Contact.FullName;
        var signOff = string.IsNullOrWhiteSpace(name) ? "Best regards," : $"Best regards,\n{name.Trim()}";
        var allowed = string.Join("\n", facts, ownerInstructions, notes, posting);
        return (new PreparedFollowUp(applicationId, context, greeting, signOff, allowed, prompt), null);
    }

    public async Task<FollowUpResult> RunAsync(PreparedFollowUp prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<FollowUpDraftResponse>(prepared.Prompt)
        {
            Validate = response => Validate(prepared.Context, response),
            Review = response => Review(prepared, response),
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return new FollowUpResult(result.Error, null);
        }

        var response = result.Value!;
        var body = new StringBuilder().AppendLine(prepared.Greeting).AppendLine();
        foreach (var paragraph in response.Paragraphs)
        {
            body.AppendLine(paragraph.Text.Trim()).AppendLine();
        }

        body.Append(prepared.SignOff);
        var draft = new FollowUpDraft(response.Subject.Trim(), body.ToString().ReplaceLineEndings("\n"), response.Notes, result.Warnings);
        await store.RecordFollowUpDraftAsync(prepared.ApplicationId, draft.FullText, cancellationToken);
        return new FollowUpResult(null, draft);
    }

    /// <summary>Facts the model may use, in English. Nothing here is inferred.</summary>
    internal static string Facts(ApplicationDetail detail, Domain.Pipeline.ApplicationContact? contact, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var application = detail.Application;
        var builder = new StringBuilder();
        builder.AppendLine($"Role: {detail.Posting.Title}");
        builder.AppendLine($"Company: {detail.Posting.Company}");
        builder.AppendLine($"Current stage: {PipelineFormat.English(application.Stage)} (since {Day(application.StageChangedAtUtc)})");
        if (application.StageVisits().FirstOrDefault(v => v.Stage.ImpliesApplied()) is { } applied)
        {
            builder.AppendLine($"Applied on: {Day(applied.EnteredAtUtc)}");
        }

        builder.AppendLine($"Today: {Day(nowUtc)}");
        builder.AppendLine($"Last contact with the company: {Day(FollowUpRules.LastContact(application, nowUtc).At)}");
        builder.AppendLine($"Follow-ups already sent in this stage: {FollowUpRules.FollowUpsInStage(application)}");

        var interviews = application.Timeline().Where(e => e.Kind == ApplicationEventKind.InterviewScheduled && e.ScheduledAtUtc is not null)
            .Select(e => Day(e.ScheduledAtUtc!.Value) + (e.ScheduledAtUtc > nowUtc ? " (upcoming)" : " (done)")).ToList();
        builder.AppendLine($"Interviews: {(interviews.Count == 0 ? "none" : string.Join("; ", interviews))}");
        var sent = application.Timeline().Where(e => e.Kind == ApplicationEventKind.DocumentSent).Select(e => ApplicationStore.ReadSnapshot(e)?.Kind).ToList();
        builder.AppendLine($"Documents sent: {(sent.Count == 0 ? "not recorded" : string.Join(", ", sent.Select(k => k == Infrastructure.Persistence.TailoredKind.Cv ? "tailored CV" : "cover letter")))}");
        builder.Append(contact is null
            ? "Recipient: unknown, write to the hiring team"
            : $"Recipient: {contact.Name}{(string.IsNullOrWhiteSpace(contact.Role) ? string.Empty : $" ({contact.Role})")}");
        return builder.ToString();
    }

    /// <summary>Structural problems: always retried, never accepted.</summary>
    internal static IReadOnlyList<string> Validate(TailoringContext context, FollowUpDraftResponse response)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(response.Subject))
        {
            problems.Add("The subject is empty.");
        }
        else if (response.Subject.Length > 120)
        {
            problems.Add("The subject is longer than 120 characters.");
        }

        if (response.Paragraphs.Count is 0 or > 4)
        {
            problems.Add("Write between 2 and 3 paragraphs.");
        }

        if (response.Paragraphs.Any(p => string.IsNullOrWhiteSpace(p.Text)))
        {
            problems.Add("A paragraph is empty.");
        }

        foreach (var text in response.Paragraphs.Select(p => p.Text).Prepend(response.Subject))
        {
            if (DraftChecks.FindPlaceholder(text) is { } placeholder)
            {
                problems.Add($"\"{placeholder}\" is a placeholder: leave unknown details out instead.");
            }
        }

        foreach (var reference in response.Paragraphs.SelectMany(p => p.Sources).Where(r => context.Index.KeyOf(r) is null))
        {
            problems.Add($"\"{reference}\" is not a reference in the CV.");
        }

        return problems;
    }

    /// <summary>Numbers and technologies that cannot be traced (soft): one retry, then shown as warnings.</summary>
    internal static IReadOnlyList<string> Review(PreparedFollowUp prepared, FollowUpDraftResponse response)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(response);
        var posting = prepared.Context.Posting;
        var texts = response.Paragraphs.Select(p => new TracedTextResponse(p.Text, p.Sources))
            .Prepend(new TracedTextResponse(response.Subject, []));
        return texts
            .SelectMany(t =>
            {
                // Naming the role or the company (e.g. "Senior .NET Engineer") is not a claim about the candidate.
                var checkedText = t with { Text = t.Text.Replace(posting.Title, " ", StringComparison.OrdinalIgnoreCase).Replace(posting.Company, " ", StringComparison.OrdinalIgnoreCase) };
                return prepared.Context.Validator.Check(prepared.Context.ToTraceable(checkedText), allowedExtraText: prepared.AllowedText, requireSources: false)
                    .Select(p => $"\"{Short(t.Text)}\": {p}");
            })
            .ToList();
    }

    private static string Day(DateTime utc) => utc.ToString("d MMMM yyyy", English);

    private static string Short(string text) => text.Length <= 60 ? text : text[..60] + "…";
}
