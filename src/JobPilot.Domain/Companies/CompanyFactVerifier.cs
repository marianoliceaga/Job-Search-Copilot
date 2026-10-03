using JobPilot.Domain.Cv;
using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Companies;

/// <summary>A fact as proposed by the model, citing a source reference (U1, T1...).</summary>
public sealed record ProposedFact(CompanyFactCategory Category, string Statement, string SourceReference, string? Quote);

/// <param name="Succeeded">False when the page could not be read (error, paywall, unsafe).</param>
public sealed record RetrievedPage(string Url, bool Succeeded);

/// <summary>
/// Deterministic "no verificado" rule. A fact is verified when its source exists and either the quote appears in the text
/// the owner pasted, or Gemini reported reading the cited URL successfully. Numbers in the statement must appear in the
/// quote. Everything else is kept but marked as not verified, with the reason.
/// </summary>
public static class CompanyFactVerifier
{
    /// <summary>Short references shown in the prompt: U1, U2... for URLs and T1, T2... for pasted texts.</summary>
    public static IReadOnlyDictionary<string, CompanySource> References(IEnumerable<CompanySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var map = new Dictionary<string, CompanySource>(StringComparer.OrdinalIgnoreCase);
        var urls = 0;
        var texts = 0;
        foreach (var source in sources)
        {
            map[source.Kind == CompanySourceKind.Url ? $"U{++urls}" : $"T{++texts}"] = source;
        }

        return map;
    }

    public static CompanyFact Verify(ProposedFact proposed, IReadOnlyDictionary<string, CompanySource> references, IReadOnlyCollection<RetrievedPage> retrieved)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(retrieved);
        var fact = new CompanyFact
        {
            Category = proposed.Category,
            Statement = proposed.Statement.Trim(),
            Quote = string.IsNullOrWhiteSpace(proposed.Quote) ? null : proposed.Quote.Trim(),
        };

        if (!references.TryGetValue(proposed.SourceReference.Trim(), out var source))
        {
            return Unverified(fact, $"Cita una fuente que no existe ({proposed.SourceReference}).");
        }

        fact.SourceId = source.Id;
        fact.SourceUrl = source.Url;
        if (fact.Quote is null)
        {
            return Unverified(fact, "No tiene una cita textual de la fuente.");
        }

        var missingNumbers = TextNormalizer.Numbers(fact.Statement).Except(TextNormalizer.Numbers(fact.Quote), StringComparer.Ordinal).ToList();
        if (missingNumbers.Count > 0)
        {
            return Unverified(fact, $"Los números {string.Join(", ", missingNumbers)} no están en la cita.");
        }

        if (source.Kind == CompanySourceKind.Text)
        {
            return EvidenceCheck.QuoteFound(fact.Quote, source.Text)
                ? Verified(fact, FactVerification.QuoteFound)
                : Unverified(fact, "La cita no aparece en el texto que pegaste.");
        }

        var key = UrlKey(source.Url);
        var page = retrieved.FirstOrDefault(p => UrlKey(p.Url) == key);
        return page switch
        {
            null => Unverified(fact, "Gemini no informó haber leído esta URL."),
            { Succeeded: false } => Unverified(fact, "Gemini no pudo leer esta URL (error, paywall o contenido bloqueado)."),
            _ => Verified(fact, FactVerification.UrlRead),
        };
    }

    /// <summary>Comparable form of a URL: scheme, "www.", trailing slash, fragment and tracking parameters ignored.</summary>
    public static string UrlKey(string? url) => url is null ? string.Empty : UrlNormalizer.Canonicalize(url.Split('#')[0]);

    private static CompanyFact Verified(CompanyFact fact, FactVerification verification)
    {
        fact.Verification = verification;
        fact.VerificationNote = null;
        return fact;
    }

    private static CompanyFact Unverified(CompanyFact fact, string reason)
    {
        fact.Verification = FactVerification.Unverified;
        fact.VerificationNote = reason;
        return fact;
    }
}
