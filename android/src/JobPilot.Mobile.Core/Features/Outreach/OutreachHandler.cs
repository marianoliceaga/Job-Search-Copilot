using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using JobPilot.Domain.Companies;
using JobPilot.Domain.Jobs;
using JobPilot.Domain.Prompts;
using JobPilot.Domain.Tailoring;
using JobPilot.Mobile.Core.Features.Companies;
using JobPilot.Mobile.Core.Features.Jobs.Analysis;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Core.Features.Outreach;

public sealed class OutreachOptions
{
    public const string SectionName = "Outreach";

    /// <summary>LinkedIn invitation note: 200 characters on free accounts, 300 on Premium (checked 2026-10-02).</summary>
    public int ConnectionNoteMaxLength { get; set; } = 200;

    public int HeadlineMaxLength { get; set; } = 220;

    public int AboutMaxLength { get; set; } = 2600;
}

public enum OutreachTone { Formal, Friendly, Direct }

public sealed record OutreachParagraphResponse(
    [property: Description("The paragraph, in English.")] string Text,
    [property: Description("References it relies on: CV (E1, E1.A2, SUM...) and company facts (F1, F2...). Empty if it says nothing about the candidate or the company.")] List<string> Sources);

public sealed record OutreachVariantResponse(
    OutreachTone Tone,
    [property: Description("E-mail subject; empty string for LinkedIn messages.")] string Subject,
    [property: Description("1-3 short paragraphs without greeting or sign-off.")] List<OutreachParagraphResponse> Paragraphs);

public sealed record OutreachResponse(
    [property: Description("Exactly three variants: Formal, Friendly and Direct.")] List<OutreachVariantResponse> Variants,
    [property: Description("In Spanish (Rioplatense): what the messages emphasise and what to check before sending, 1-2 sentences.")] string Notes);

/// <param name="PostingId">Offer the message is about (optional).</param>
/// <param name="CompanyId">Company profile whose verified facts may be used (optional; found from the offer otherwise).</param>
public sealed record OutreachInput(
    Guid? PostingId,
    Guid? CompanyId,
    OutreachChannel Channel,
    OutreachLength Length,
    string? RecipientName,
    string? RecipientRole,
    string? Instructions);

public sealed record PreparedOutreach(
    OutreachInput Input,
    CvContext Context,
    JobPosting? Posting,
    CompanyProfile? Company,
    IReadOnlyDictionary<string, CompanyFact> Facts,
    string AllowedText,
    RenderedPrompt Prompt);

public sealed record OutreachVariant(OutreachTone Tone, string? Subject, string Body, IReadOnlyList<string> Warnings);

public sealed record OutreachDraftSet(IReadOnlyList<OutreachVariant> Variants, string? Notes);

public sealed record OutreachResult(LlmError? Error, long? RecordId, OutreachDraftSet? Drafts);

/// <summary>
/// Drafts outreach messages in three tones. Claims about the candidate must trace to the CV and claims about the company
/// to verified facts or the posting. The owner copies and sends them: the app never sends messages.
/// </summary>
public sealed class OutreachHandler(
    TailoringContextFactory contexts,
    CompanyStore companies,
    IDbContextFactory<MobileDbContext> dbFactory,
    PromptLibrary prompts,
    ILlmClient llm,
    IOptions<OutreachOptions> options,
    TimeProvider time)
{
    public async Task<(PreparedOutreach? Prepared, string? Error)> PrepareAsync(OutreachInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.PostingId is null && input.CompanyId is null)
        {
            return (null, "Elegí una oferta o una empresa.");
        }

        var (context, error) = await contexts.CreateForCvAsync(cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        JobPosting? posting = null;
        if (input.PostingId is { } postingId)
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            posting = await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postingId, cancellationToken);
            if (posting is null)
            {
                return (null, "La oferta no existe.");
            }
        }

        var company = input.CompanyId is { } companyId
            ? await companies.GetAsync(companyId, cancellationToken)
            : await companies.FindByCompanyAsync(posting!.Company, cancellationToken);

        // Only verified facts reach the prompt: unverified ones could be wrong.
        var facts = (company?.Facts ?? []).Where(f => f.IsVerified)
            .Select((f, i) => (Ref: $"F{(i + 1).ToString(CultureInfo.InvariantCulture)}", Fact: f))
            .ToDictionary(x => x.Ref, x => x.Fact, StringComparer.OrdinalIgnoreCase);
        var factsText = facts.Count == 0 ? "(none)" : string.Join("\n", facts.Select(f => $"{f.Key}: {f.Value.Statement} (\"{f.Value.Quote}\")"));
        var postingText = posting is null ? "(no specific offer)" : JobAnalysisHandler.PostingText(posting);
        var companyName = company?.Name ?? posting?.Company ?? string.Empty;

        var recipient = new StringBuilder();
        recipient.AppendLine($"Channel: {ChannelDescription(input.Channel)}");
        recipient.AppendLine($"Company: {companyName}");
        recipient.Append(string.IsNullOrWhiteSpace(input.RecipientName)
            ? "Recipient: unknown name (do not invent one)"
            : $"Recipient: {input.RecipientName.Trim()}{(string.IsNullOrWhiteSpace(input.RecipientRole) ? string.Empty : $", {input.RecipientRole.Trim()}")}");
        if (string.IsNullOrWhiteSpace(input.RecipientName) && !string.IsNullOrWhiteSpace(input.RecipientRole))
        {
            recipient.Append($" ({input.RecipientRole.Trim()})");
        }

        var instructions = string.IsNullOrWhiteSpace(input.Instructions) ? "(none)" : input.Instructions.Trim();
        var prompt = prompts.Get(LlmUseCases.OutreachMessage).Render(new Dictionary<string, string>
        {
            ["format"] = Format(input.Channel, input.Length),
            ["recipient"] = recipient.ToString(),
            ["instructions"] = instructions,
            ["cv"] = context.CvWithReferences(),
            ["facts"] = factsText,
            ["posting"] = postingText,
        });
        var allowed = string.Join("\n", recipient, instructions, factsText, postingText);
        return (new PreparedOutreach(input, context, posting, company, facts, allowed, prompt), null);
    }

    public async Task<OutreachResult> RunAsync(PreparedOutreach prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<OutreachResponse>(prepared.Prompt)
        {
            Validate = response => Validate(prepared, response),
            Review = response => response.Variants.SelectMany(v => Warnings(prepared, v).Select(w => $"{v.Tone}: {w}")).ToList(),
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return new OutreachResult(result.Error, null, null);
        }

        var drafts = new OutreachDraftSet(
            result.Value!.Variants.OrderBy(v => v.Tone).Select(v => new OutreachVariant(
                v.Tone,
                prepared.Input.Channel == OutreachChannel.Email ? v.Subject.Trim() : null,
                Compose(prepared, v.Paragraphs.Select(p => p.Text)),
                Warnings(prepared, v))).ToList(),
            result.Value.Notes);

        var record = new OutreachRecord
        {
            CompanyId = prepared.Company?.Id,
            JobPostingId = prepared.Posting?.Id,
            Channel = prepared.Input.Channel,
            Length = prepared.Input.Length,
            RecipientName = Clean(prepared.Input.RecipientName),
            RecipientRole = Clean(prepared.Input.RecipientRole),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            PromptId = prepared.Prompt.PromptId,
            Model = result.Model,
            Json = JsonSerializer.Serialize(drafts, DomainJson.Options),
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.OutreachMessages.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return new OutreachResult(null, record.Id, drafts);
    }

    /// <summary>Greeting and sign-off come from the app; a connection note has no sign-off (space is short).</summary>
    internal static string Compose(PreparedOutreach prepared, IEnumerable<string> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var first = prepared.Input.RecipientName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var greeting = first is null ? "Hello," : $"Hi {first},";
        var body = string.Join("\n\n", paragraphs.Select(p => p.Trim()).Where(p => p.Length > 0));
        if (prepared.Input.Channel == OutreachChannel.LinkedInConnection)
        {
            return $"{greeting} {body.ReplaceLineEndings(" ")}";
        }

        var name = prepared.Context.Cv.Contact.FullName?.Trim();
        var signOff = prepared.Input.Channel == OutreachChannel.Email ? "Best regards," : "Best,";
        return $"{greeting}\n\n{body}\n\n{signOff}" + (string.IsNullOrEmpty(name) ? string.Empty : $"\n{name}");
    }

    /// <summary>Structural problems: always retried, never accepted.</summary>
    internal IReadOnlyList<string> Validate(PreparedOutreach prepared, OutreachResponse response)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(response);
        var problems = new List<string>();
        var tones = response.Variants.Select(v => v.Tone).ToList();
        if (tones.Count != 3 || tones.Distinct().Count() != 3)
        {
            problems.Add("Return exactly three variants: Formal, Friendly and Direct.");
        }

        foreach (var variant in response.Variants)
        {
            if (variant.Paragraphs.Count is 0 or > 3 || variant.Paragraphs.Any(p => string.IsNullOrWhiteSpace(p.Text)))
            {
                problems.Add($"{variant.Tone}: write 1-3 non-empty paragraphs.");
            }

            if (prepared.Input.Channel == OutreachChannel.Email && string.IsNullOrWhiteSpace(variant.Subject))
            {
                problems.Add($"{variant.Tone}: an e-mail needs a subject.");
            }

            foreach (var text in variant.Paragraphs.Select(p => p.Text).Prepend(variant.Subject))
            {
                if (DraftChecks.FindPlaceholder(text) is { } placeholder)
                {
                    problems.Add($"{variant.Tone}: \"{placeholder}\" is a placeholder; leave unknown details out.");
                }
            }

            foreach (var reference in variant.Paragraphs.SelectMany(p => p.Sources).Where(r => prepared.Context.Index.KeyOf(r) is null && !prepared.Facts.ContainsKey(r.Trim())))
            {
                problems.Add($"{variant.Tone}: \"{reference}\" is not a CV or company fact reference.");
            }

            var max = options.Value.ConnectionNoteMaxLength;
            if (prepared.Input.Channel == OutreachChannel.LinkedInConnection && Compose(prepared, variant.Paragraphs.Select(p => p.Text)).Length > max)
            {
                problems.Add($"{variant.Tone}: a LinkedIn invitation note must fit in {max} characters including \"Hi <name>, \"; shorten it.");
            }
        }

        return problems;
    }

    /// <summary>Numbers and technologies that cannot be traced to the CV, the cited facts, the posting or the owner's input.</summary>
    internal static IReadOnlyList<string> Warnings(PreparedOutreach prepared, OutreachVariantResponse variant)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(variant);
        var warnings = new List<string>();
        var naming = new[] { prepared.Posting?.Title, prepared.Posting?.Company, prepared.Company?.Name }.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        // Only e-mails have a subject; LinkedIn messages ignore it.
        var texts = prepared.Input.Channel == OutreachChannel.Email && !string.IsNullOrWhiteSpace(variant.Subject)
            ? variant.Paragraphs.Prepend(new OutreachParagraphResponse(variant.Subject, []))
            : variant.Paragraphs;
        foreach (var paragraph in texts)
        {
            var cvSources = paragraph.Sources.Where(r => prepared.Context.Index.KeyOf(r) is not null).ToList();
            var factText = string.Join("\n", paragraph.Sources.Where(r => prepared.Facts.ContainsKey(r.Trim())).Select(r => prepared.Facts[r.Trim()].Statement + " " + prepared.Facts[r.Trim()].Quote));
            // Naming the role or the company is not a claim about the candidate.
            var text = naming.Aggregate(paragraph.Text, (current, name) => current.Replace(name!, " ", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }
            var traceable = prepared.Context.ToTraceable(new TracedTextResponse(text, cvSources));
            warnings.AddRange(prepared.Context.Validator.Check(traceable, allowedExtraText: prepared.AllowedText, requireSources: false, allowedTechnologyText: factText)
                .Select(p => $"\"{Short(paragraph.Text)}\": {p}"));
        }

        return warnings;
    }

    private static string ChannelDescription(OutreachChannel channel) => channel switch
    {
        OutreachChannel.LinkedInConnection => "LinkedIn invitation note",
        OutreachChannel.LinkedInMessage => "LinkedIn message",
        _ => "E-mail",
    };

    private string Format(OutreachChannel channel, OutreachLength length) => (channel, length) switch
    {
        (OutreachChannel.LinkedInConnection, _) =>
            $"LinkedIn invitation note: one paragraph; the whole note, including \"Hi <name>, \", must fit in {options.Value.ConnectionNoteMaxLength} characters.",
        (_, OutreachLength.Short) => $"{ChannelDescription(channel)}, short: at most 70 words per variant.",
        _ => $"{ChannelDescription(channel)}, medium: at most 140 words per variant.",
    };

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Short(string text) => text.Length <= 60 ? text : text[..60] + "…";
}
