using System.ComponentModel;
using System.Text;
using JobPilot.Domain.Companies;
using JobPilot.Domain.Prompts;
using JobPilot.Mobile.Core.Infrastructure.Llm;

namespace JobPilot.Mobile.Core.Features.Companies;

public sealed record CompanyFactResponse(
    CompanyFactCategory Category,
    [property: Description("The fact in Spanish, short. Numbers must appear in the quote.")] string Statement,
    [property: Description("Reference of the single source it comes from: U1, U2... (URLs) or T1, T2... (pasted texts).")] string Source,
    [property: Description("Exact quote from that source, in its original language, one sentence or less.")] string Quote);

public sealed record CompanyResearchResponse(
    [property: Description("Facts found in the sources, most relevant first, at most 25.")] List<CompanyFactResponse> Facts,
    [property: Description("In Spanish: topics relevant to a job seeker that the sources do not cover (e.g. \"financiamiento\").")] List<string> Gaps);

public sealed record PreparedCompanyResearch(CompanyProfile Profile, IReadOnlyDictionary<string, CompanySource> References, bool ReadsUrls, RenderedPrompt Prompt);

public sealed record CompanyResearchResult(LlmError? Error, int Facts, int Verified, IReadOnlyList<LlmRetrievedUrl> RetrievedUrls);

/// <summary>
/// Researches a company from the sources the owner chose (URL context + pasted texts) and verifies every fact
/// deterministically: unverifiable facts are kept but marked "no verificado".
/// </summary>
public sealed class CompanyResearchHandler(CompanyStore store, PromptLibrary prompts, ILlmClient llm)
{
    public const int MaxFacts = 40;

    public async Task<(PreparedCompanyResearch? Prepared, string? Error)> PrepareAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var profile = await store.GetAsync(companyId, cancellationToken);
        if (profile is null)
        {
            return (null, "La empresa no existe.");
        }

        if (profile.Sources.Count == 0)
        {
            return (null, "Agregá al menos una fuente: una URL pública de la empresa o un texto pegado.");
        }

        var references = CompanyFactVerifier.References(profile.Sources);
        var urls = new StringBuilder();
        var texts = new StringBuilder();
        foreach (var (reference, source) in references)
        {
            if (source.Kind == CompanySourceKind.Url)
            {
                urls.AppendLine($"{reference}: {source.Url}");
            }
            else
            {
                texts.AppendLine($"--- {reference}{(source.Url is null ? string.Empty : $" (from {source.Url})")}").AppendLine(source.Text).AppendLine();
            }
        }

        var prompt = prompts.Get(LlmUseCases.CompanyResearch).Render(new Dictionary<string, string>
        {
            ["company"] = profile.Name + (profile.Website is null ? string.Empty : $" ({profile.Website})"),
            ["urls"] = urls.Length == 0 ? "(none)" : urls.ToString().Trim(),
            ["texts"] = texts.Length == 0 ? "(none)" : texts.ToString().Trim(),
        });
        return (new PreparedCompanyResearch(profile, references, urls.Length > 0, prompt), null);
    }

    public async Task<CompanyResearchResult> RunAsync(PreparedCompanyResearch prepared, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await llm.GenerateAsync(new LlmRequest<CompanyResearchResponse>(prepared.Prompt)
        {
            Validate = Validate,
            UseUrlContext = prepared.ReadsUrls,
        }, cancellationToken);
        if (!result.IsSuccess)
        {
            return new CompanyResearchResult(result.Error, 0, 0, result.RetrievedUrls);
        }

        var pages = result.RetrievedUrls.Select(u => new RetrievedPage(u.Url, u.Succeeded)).ToList();
        var facts = result.Value!.Facts
            .Select(f => CompanyFactVerifier.Verify(new ProposedFact(f.Category, f.Statement, f.Source, f.Quote), prepared.References, pages))
            .ToList();
        await store.SaveResearchAsync(prepared.Profile.Id, facts, result.Value.Gaps, prepared.Prompt.PromptId, result.Model, cancellationToken);
        return new CompanyResearchResult(null, facts.Count, facts.Count(f => f.IsVerified), result.RetrievedUrls);
    }

    /// <summary>Structural problems only; traceability is checked per fact and never hidden.</summary>
    internal static IReadOnlyList<string> Validate(CompanyResearchResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var problems = new List<string>();
        if (response.Facts.Count > MaxFacts)
        {
            problems.Add($"Return at most {MaxFacts} facts.");
        }

        if (response.Facts.Any(f => string.IsNullOrWhiteSpace(f.Statement)))
        {
            problems.Add("A fact has an empty statement.");
        }

        return problems;
    }
}
