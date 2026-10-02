using JobPilot.Domain.Cv;

namespace JobPilot.Domain.Tests.Cv;

public class PartialDateTests
{
    [Theory]
    [InlineData("2021", 2021, null)]
    [InlineData("2021-03", 2021, 3)]
    [InlineData(" 1999-12 ", 1999, 12)]
    public void Parses_year_and_year_month(string text, int year, int? month)
    {
        Assert.True(PartialDate.TryParse(text, out var date));
        Assert.Equal(new PartialDate(year, month), date);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("03/2021")]
    [InlineData("2021-13")]
    [InlineData("2021-00")]
    [InlineData("2021-3")]
    [InlineData("1850")]
    [InlineData("March 2021")]
    public void Rejects_other_formats(string? text)
    {
        Assert.False(PartialDate.TryParse(text, out _));
    }

    [Fact]
    public void Formats_back_to_text()
    {
        Assert.Equal("2021-03", new PartialDate(2021, 3).ToString());
        Assert.Equal("2021", new PartialDate(2021).ToString());
    }

    [Fact]
    public void Orders_year_only_before_months_of_the_same_year()
    {
        Assert.True(new PartialDate(2021) < new PartialDate(2021, 1));
        Assert.True(new PartialDate(2020, 12) < new PartialDate(2021));
        Assert.True(new PartialDate(2021, 5) > new PartialDate(2021, 4));
    }
}
