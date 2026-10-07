using System.ComponentModel;
using JobPilot.Domain.Prompts;
using JobPilot.Domain.Tailoring;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Core.Features.Outreach;

public sealed record LinkedInReviewResponse(
    [property: Description("In Spanish (Rioplatense): assessment of the current headline and About against the CV and target roles, 3-6 sentences.")] string Assessment,
    [property: Description("In Spanish: concrete improvements, one per item.")] List<string> Improvements,
    [property: Description("Three alternative headlines in English, each citing the CV references it relies on.")] List<TracedTextResponse> Headlines,
    [property: Description("Proposed About section in English: 3-5 paragraphs, each citing its CV references.")] List<TracedTextResponse> About);

public sealed record LinkedInReviewInput(string? Headline, string? About, string? Focus);

public sealed record PreparedLinkedInReview(LinkedInReviewInput Input, CvContext Context, RenderedPrompt Prompt);

/// <summary>A proposed text with the problems found by the traceability check (empty when it traces to the CV).</summary>
public sealed record ReviewedText(string Text, string Sources, IReadOnlyList<string> Problems);

/// <summary>Last review, stored locally so the owner can come back to it.</summary>
public sealed record LinkedInReview(
    LinkedInReviewInput Input,
    string Assessment,
    IReadOnlyList<string> Improvements,
    IReadOnlyList<ReviewedText> Headlines,
    IReadOnlyList<ReviewedText> About,
    int MasterCvVersion,
    DateTime CreatedAtUtc,
    string Model);

/// <summary>
/// Reviews the LinkedIn headline and About the owner pasted (LinkedIn is never accessed) and proposes texts traceable to
/// the master CV. Copy only.
/// </summary>
public sealed class LinkedInReviewHandler(
    TailoringContextFactory contexts,
    SettingsDocumentStore documents,
    PromptLibrary prompts,
    ILlmClient llm,
    IOptions<OutreachOptions> options,
    TimeProvider time)
{
    private const string Key = "linkedin-review";

    public Task<LinkedInReview?> LastAsync(CancellationToken cancellationToken) => documents.GetAsync<LinkedInReview>(Key, cancellationToken);

    public async Task<(PreparedLinkedInReview? Prepared, string? Error)> PrepareAsync(LinkedInReviewInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (context, error) = await contexts.CreateForCvAsync(cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        var prompt = prompts.Get(LlmUseCases.LinkedInReview).Render(new Dictionary<string, string>
        {
            ["headline_max"] = options.Value.HeadlineMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["about_max"] = options.Value.AboutMaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["headline"] = Value(input.Headline),
            ["about"] = Value(input.About),
            ["focus"] = Value(input.Focus),
            ["cv"] = context.CvWithReferences(),
            ["emphases"] = context.Emphases(),
        });
        return (new PreparedLinkedInReview(input, context, prompt), null);
    }

    public async Task<(LinkedInReview? Review, LlmError? Error)> RunAsync(PreparedLinkedInReview prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<LinkedInReviewResponse>(prepared.Prompt)
        {
            Validate = response => Validate(prepared.Context, response),
            Review = response => response.Headlines.Concat(response.About)
                .SelectMany(t => Check(prepared.Context, t).Select(p => $"\"{t.Text}\": {p}")).ToList(),
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return (null, result.Error);
        }

        var value = result.Value!;
        var review = new LinkedInReview(
            prepared.Input,
            value.Assessment,
            value.Improvements,
            value.Headlines.Select(t => Reviewed(prepared.Context, t)).ToList(),
            value.About.Select(t => Reviewed(prepared.Context, t)).ToList(),
            prepared.Context.CvVersion,
            time.GetUtcNow().UtcDateTime,
            result.Model);
        await documents.SetAsync(Key, review, cancellationToken);
        return (review, null);
    }

    internal IReadOnlyList<string> Validate(CvContext context, LinkedInReviewResponse response)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        var problems = new List<string>();
        if (response.Headlines.Count is 0 or > 5)
        {
            problems.Add("Propose three headlines.");
        }

        foreach (var headline in response.Headlines.Where(h => h.Text.Trim().Length > options.Value.HeadlineMaxLength))
        {
            problems.Add($"Headline \"{headline.Text}\" is longer than {options.Value.HeadlineMaxLength} characters.");
        }

        var aboutLength = string.Join("\n\n", response.About.Select(a => a.Text.Trim())).Length;
        if (response.About.Count == 0 || aboutLength > options.Value.AboutMaxLength)
        {
            problems.Add($"The About section must have 3-5 paragraphs and at most {options.Value.AboutMaxLength} characters (it has {aboutLength}).");
        }

        foreach (var text in response.Headlines.Concat(response.About))
        {
            if (DraftChecks.FindPlaceholder(text.Text) is { } placeholder)
            {
                problems.Add($"\"{placeholder}\" is a placeholder; leave unknown details out.");
            }

            foreach (var reference in text.Sources.Where(r => context.Index.KeyOf(r) is null))
            {
                problems.Add($"\"{reference}\" is not a reference in the CV.");
            }
        }

        return problems;
    }

    private static IReadOnlyList<string> Check(CvContext context, TracedTextResponse text) =>
        context.Validator.Check(context.ToTraceable(text));

    private static ReviewedText Reviewed(CvContext context, TracedTextResponse text)
    {
        var traceable = context.ToTraceable(text);
        return new ReviewedText(traceable.Text, TailoringContext.Describe(context.Index, traceable.Sources), Check(context, text));
    }

    private static string Value(string? text) => string.IsNullOrWhiteSpace(text) ? "(not provided)" : text.Trim();
}
