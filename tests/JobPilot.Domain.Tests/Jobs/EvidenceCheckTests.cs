using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Tests.Jobs;

public class EvidenceCheckTests
{
    private const string Posting = "Compensation: USD 120k – 150k per year, paid as a contractor.\nLocation: LATAM.";

    [Theory]
    [InlineData("USD 120k - 150k per year", true)]
    [InlineData("usd 120K – 150K PER YEAR", true)]
    [InlineData("USD 130k – 160k per year", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Finds_quotes_ignoring_formatting(string? quote, bool expected)
    {
        Assert.Equal(expected, EvidenceCheck.QuoteFound(quote, Posting));
    }

    [Theory]
    [InlineData(120_000, "USD 120k – 150k", true)]
    [InlineData(150_000, "USD 120k – 150k", true)]
    [InlineData(150_000, "$150,000 a year", true)]
    [InlineData(6_500, "6.500 USD por mes", false)]
    [InlineData(6_500, "6500 USD per month", true)]
    [InlineData(200_000, "USD 120k – 150k", false)]
    public void Recognises_amounts_written_in_full_or_with_k(double amount, string quote, bool expected)
    {
        Assert.Equal(expected, EvidenceCheck.AmountInQuote((decimal)amount, quote));
    }
}
