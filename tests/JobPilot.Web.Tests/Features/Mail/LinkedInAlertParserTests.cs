using JobPilot.Web.Features.Mail;

using static JobPilot.Web.Tests.Features.Mail.MailFixtures;

namespace JobPilot.Web.Tests.Features.Mail;

public class LinkedInAlertParserTests
{
    [Fact]
    public void Reads_title_company_location_workplace_pay_and_flags_from_each_card()
    {
        var message = AlertMessage("m1", "Senior .NET in Argentina", AlertHtml(
            AlertCard("4000000001", "Senior Software Engineer - .NET", "Fabrikam · United States (Remote)", "$105K-$215K / year", "Actively recruiting"),
            AlertCard("4000000002", "Senior .Net Developer", "Contoso · Buenos Aires, Argentina (Hybrid)")));

        var result = LinkedInAlertParser.Parse(message, "t1");

        Assert.False(result.UsedTextFallback);
        Assert.Equal(2, result.Listings.Count);
        var first = result.Listings[0];
        Assert.Equal(("4000000001", "Senior Software Engineer - .NET", "Fabrikam", "United States", "remote"), (first.JobId, first.Title, first.Company, first.Location, first.Workplace));
        Assert.Equal((105_000m, 215_000m, "USD", "year"), (first.Pay!.Low, first.Pay.High, first.Pay.Currency, first.Pay.Period));
        Assert.Equal(["Actively recruiting"], first.Flags);
        Assert.Equal("Senior .NET in Argentina", first.AlertName);
        Assert.Equal("t1", first.ThreadId);
        Assert.Equal("https://www.linkedin.com/jobs/view/4000000001/", first.Url);
        Assert.Equal(("hybrid", "Buenos Aires, Argentina"), (result.Listings[1].Workplace, result.Listings[1].Location));
    }

    [Fact]
    public void Falls_back_to_the_text_part_when_the_html_has_no_cards()
    {
        var text = """
            Your job alert for Backend in Argentina
            New jobs match your preferences.

            Senior Software Engineer (.NET) - Remote - Latin America
            Northwind
            Buenos Aires

            This company is actively hiring
            View job: https://www.linkedin.com/comm/jobs/view/4000000003/?trackingId=x

            ---------------------------------------------------------

            Sr. Backend Developer
            Tailspin
            Argentina
            View job: https://www.linkedin.com/comm/jobs/view/4000000004/?trackingId=y

            ---------------------------------------------------------
            """;

        var result = LinkedInAlertParser.Parse(AlertMessage("m1", "x", "<html><body><p>changed layout</p></body></html>", text), "t1");

        Assert.True(result.UsedTextFallback);
        Assert.Equal(["4000000003", "4000000004"], result.Listings.Select(l => l.JobId));
        Assert.Equal(("Northwind", "Buenos Aires", ""), (result.Listings[0].Company, result.Listings[0].Location, result.Listings[0].Workplace));
        Assert.Equal(["This company is actively hiring"], result.Listings[0].Flags);
        Assert.Equal("Backend in Argentina", result.Listings[0].AlertName);
    }

    [Theory]
    [InlineData("$105K-$215K / year", 105_000, 215_000, "USD", "year")]
    [InlineData("€20K-€25K / year", 20_000, 25_000, "EUR", "year")]
    [InlineData("$40/hr", 40, 40, "USD", "hour")]
    [InlineData("USD 3,500 - 4,500 / month", 3_500, 4_500, "USD", "month")]
    [InlineData("ARS 1.500.000 / mes", 1_500_000, 1_500_000, "ARS", "month")]
    public void Parses_pay_ranges(string text, decimal low, decimal high, string currency, string period)
    {
        var pay = LinkedInAlertParser.ParsePay(text);

        Assert.NotNull(pay);
        Assert.Equal((low, high, currency, period), (pay.Low, pay.High, pay.Currency, pay.Period));
    }

    [Theory]
    [InlineData("Contoso · Mendoza, Argentina (On-site)", "Contoso", "Mendoza, Argentina", "onsite")]
    [InlineData("Contoso · Argentina", "Contoso", "Argentina", "")]
    [InlineData("Contoso · Córdoba (Remoto)", "Contoso", "Córdoba", "remote")]
    public void Splits_company_location_and_workplace(string line, string company, string location, string workplace)
    {
        Assert.Equal((company, location, workplace), LinkedInAlertParser.SplitCompanyLine(line));
    }
}
