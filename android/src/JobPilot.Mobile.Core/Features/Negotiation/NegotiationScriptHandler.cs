using System.ComponentModel;
using JobPilot.Domain.Negotiation;
using JobPilot.Domain.Prompts;
using JobPilot.Domain.Tailoring;
using JobPilot.Mobile.Core.Features.Jobs.Analysis;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Mobile.Core.Features.Negotiation;

public sealed record ObjectionResponse(
    [property: Description("What the recruiter may say, in English.")] string Objection,
    [property: Description("How the candidate answers, in English.")] string Response);

public sealed record NegotiationScriptResponse(
    [property: Description("Opening line, English.")] string Opening,
    [property: Description("The ask: a concrete number or range from the facts, English.")] string Ask,
    [property: Description("2-4 justification points; each cites the CV references it relies on.")] List<TracedTextResponse> Justification,
    [property: Description("3-5 likely objections with a response each.")] List<ObjectionResponse> Objections,
    [property: Description("Closing line, English.")] string Closing,
    [property: Description("In Spanish (Rioplatense): what to watch out for, 2-3 sentences.")] string Notes);

public sealed record ScriptLine(string Label, string Text, string? Sources, IReadOnlyList<string> Problems);

public sealed record NegotiationScript(long OfferId, IReadOnlyList<ScriptLine> Lines, string? Notes, DateTime CreatedAtUtc, string Model);

public sealed record PreparedNegotiationScript(CompensationOffer Offer, CvContext Context, string Facts, RenderedPrompt Prompt);

/// <summary>
/// Writes a negotiation script from the owner's numbers and the master CV. Every number must come from the facts (offers,
/// targets) or the cited CV elements; anything else is flagged.
/// </summary>
public sealed class NegotiationScriptHandler(
    OfferStore offers,
    TailoringContextFactory contexts,
    IDbContextFactory<MobileDbContext> dbFactory,
    SettingsDocumentStore documents,
    PromptLibrary prompts,
    ILlmClient llm,
    TimeProvider time)
{
    public Task<NegotiationScript?> LastAsync(long offerId, CancellationToken cancellationToken) =>
        documents.GetAsync<NegotiationScript>(Key(offerId), cancellationToken);

    public async Task<(PreparedNegotiationScript? Prepared, string? Error)> PrepareAsync(long offerId, string? goals, CancellationToken cancellationToken)
    {
        var offer = await offers.GetAsync(offerId, cancellationToken);
        if (offer is null)
        {
            return (null, "La oferta no existe.");
        }

        var (context, error) = await contexts.CreateForCvAsync(cancellationToken);
        if (context is null)
        {
            return (null, error);
        }

        var facts = await offers.FactsAsync(offer, cancellationToken);
        var posting = await PostingTextAsync(offer, cancellationToken);
        var prompt = prompts.Get(LlmUseCases.NegotiationScript).Render(new Dictionary<string, string>
        {
            ["facts"] = facts,
            ["goals"] = string.IsNullOrWhiteSpace(goals) ? "(none)" : goals.Trim(),
            ["cv"] = context.CvWithReferences(),
            ["posting"] = posting,
        });
        return (new PreparedNegotiationScript(offer, context, facts + "\n" + goals, prompt), null);
    }

    public async Task<(NegotiationScript? Script, LlmError? Error)> RunAsync(PreparedNegotiationScript prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<NegotiationScriptResponse>(prepared.Prompt)
        {
            Validate = response => Validate(prepared.Context, response),
            Review = response => Lines(prepared, response).SelectMany(l => l.Problems.Select(p => $"{l.Label}: {p}")).ToList(),
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return (null, result.Error);
        }

        var script = new NegotiationScript(prepared.Offer.Id, Lines(prepared, result.Value!), result.Value!.Notes, time.GetUtcNow().UtcDateTime, result.Model);
        await documents.SetAsync(Key(prepared.Offer.Id), script, cancellationToken);
        return (script, null);
    }

    internal static IReadOnlyList<ScriptLine> Lines(PreparedNegotiationScript prepared, NegotiationScriptResponse response)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(response);
        var context = prepared.Context;
        ScriptLine Plain(string label, string text) =>
            new(label, text.Trim(), null, context.Validator.Check(new TraceableText { Text = text }, allowedExtraText: prepared.Facts, requireSources: false));

        var lines = new List<ScriptLine> { Plain("Apertura", response.Opening), Plain("Pedido", response.Ask) };
        foreach (var point in response.Justification)
        {
            var traceable = context.ToTraceable(point);
            lines.Add(new ScriptLine("Justificación", traceable.Text, TailoringContext.Describe(context.Index, traceable.Sources),
                context.Validator.Check(traceable, allowedExtraText: prepared.Facts)));
        }

        foreach (var objection in response.Objections)
        {
            lines.Add(new ScriptLine("Objeción", objection.Objection.Trim(), null, []));
            lines.Add(Plain("Respuesta", objection.Response));
        }

        lines.Add(Plain("Cierre", response.Closing));
        return lines;
    }

    internal static IReadOnlyList<string> Validate(CvContext context, NegotiationScriptResponse response)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(response);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(response.Ask) || string.IsNullOrWhiteSpace(response.Opening) || string.IsNullOrWhiteSpace(response.Closing))
        {
            problems.Add("Opening, ask and closing are required.");
        }

        if (response.Justification.Count == 0 || response.Objections.Count == 0)
        {
            problems.Add("Include justification points and objections.");
        }

        foreach (var reference in response.Justification.SelectMany(j => j.Sources).Where(r => context.Index.KeyOf(r) is null))
        {
            problems.Add($"\"{reference}\" is not a reference in the CV.");
        }

        foreach (var text in new[] { response.Opening, response.Ask, response.Closing }.Concat(response.Objections.Select(o => o.Response)))
        {
            if (DraftChecks.FindPlaceholder(text) is { } placeholder)
            {
                problems.Add($"\"{placeholder}\" is a placeholder; use the facts or leave it out.");
            }
        }

        return problems;
    }

    private async Task<string> PostingTextAsync(CompensationOffer offer, CancellationToken cancellationToken)
    {
        if (offer.JobPostingId is not { } postingId)
        {
            return "(not provided)";
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var posting = await db.JobPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postingId, cancellationToken);
        return posting is null ? "(not provided)" : JobAnalysisHandler.PostingText(posting);
    }

    private static string Key(long offerId) => $"negotiation-script-{offerId}";
}
