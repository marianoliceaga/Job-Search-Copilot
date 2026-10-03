using JobPilot.Domain.Companies;
using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Tests.Companies;

public class CompanyFactVerifierTests
{
    private static readonly CompanySource About = new() { Kind = CompanySourceKind.Url, Url = "https://www.contoso.example.com/about/" };
    private static readonly CompanySource Paywalled = new() { Kind = CompanySourceKind.Url, Url = "https://news.example.com/contoso-raises" };
    private static readonly CompanySource Pasted = new()
    {
        Kind = CompanySourceKind.Text, Url = "https://contoso.example.com/careers", Text = "Contoso is a remote-first company with 120 employees in 14 countries.",
    };

    private static readonly IReadOnlyDictionary<string, CompanySource> References = CompanyFactVerifier.References([About, Paywalled, Pasted]);

    private static readonly RetrievedPage[] Retrieved =
    [
        new("http://contoso.example.com/about#team", true),
        new("https://news.example.com/contoso-raises", false),
    ];

    private static CompanyFact Verify(string reference, string statement, string? quote, CompanyFactCategory category = CompanyFactCategory.Overview) =>
        CompanyFactVerifier.Verify(new ProposedFact(category, statement, reference, quote), References, Retrieved);

    [Fact]
    public void References_number_urls_and_texts_separately()
    {
        Assert.Equal(["U1", "U2", "T1"], References.Keys);
        Assert.Same(Pasted, References["t1"]);
    }

    [Fact]
    public void A_fact_from_a_url_gemini_read_is_verified_and_keeps_the_url()
    {
        var fact = Verify("U1", "Desarrolla software de logística.", "Contoso builds logistics software.");

        Assert.Equal(FactVerification.UrlRead, fact.Verification);
        Assert.Equal(About.Url, fact.SourceUrl);
        Assert.Equal(About.Id, fact.SourceId);
        Assert.Null(fact.VerificationNote);
    }

    [Fact]
    public void A_url_gemini_did_not_read_is_not_verified()
    {
        var unread = CompanyFactVerifier.Verify(
            new ProposedFact(CompanyFactCategory.News, "Levantó una ronda.", "U1", "Contoso raised a round."),
            References, [new RetrievedPage("https://contoso.example.com/blog", true)]);
        var failed = Verify("U2", "Levantó una ronda.", "Contoso raised a round.", CompanyFactCategory.Funding);

        Assert.Equal((FactVerification.Unverified, "Gemini no informó haber leído esta URL."), (unread.Verification, unread.VerificationNote));
        Assert.Equal(FactVerification.Unverified, failed.Verification);
        Assert.Contains("no pudo leer", failed.VerificationNote, StringComparison.Ordinal);
        Assert.Equal(Paywalled.Url, failed.SourceUrl);
    }

    [Fact]
    public void Pasted_text_requires_the_quote_to_appear_in_it()
    {
        var found = Verify("T1", "Es remote-first.", "remote-first   company");
        var missing = Verify("T1", "Es remote-first.", "fully remote since 2015");

        Assert.Equal(FactVerification.QuoteFound, found.Verification);
        Assert.Equal(Pasted.Url, found.SourceUrl);
        Assert.Equal((FactVerification.Unverified, "La cita no aparece en el texto que pegaste."), (missing.Verification, missing.VerificationNote));
    }

    [Fact]
    public void Numbers_must_appear_in_the_quote()
    {
        var invented = Verify("T1", "Tiene 150 empleados en 14 países.", "120 employees in 14 countries");
        var faithful = Verify("T1", "Tiene 120 empleados en 14 países.", "120 employees in 14 countries");

        Assert.Equal(FactVerification.Unverified, invented.Verification);
        Assert.Contains("150", invented.VerificationNote, StringComparison.Ordinal);
        Assert.True(faithful.IsVerified);
    }

    [Fact]
    public void Unknown_sources_and_missing_quotes_are_not_verified()
    {
        Assert.Equal("Cita una fuente que no existe (U9).", Verify("U9", "x", "y").VerificationNote);
        Assert.Equal("No tiene una cita textual de la fuente.", Verify("U1", "x", " ").VerificationNote);
    }

    [Fact]
    public void Company_keys_ignore_legal_suffixes_and_case()
    {
        Assert.Equal("contoso cloud", JobFingerprint.CompanyKey("Contoso Cloud, Inc."));
        Assert.Equal(JobFingerprint.CompanyKey("CONTOSO CLOUD LLC"), CompanyProfile.Create("Contoso Cloud", null, DateTime.UtcNow).NameKey);
    }
}
