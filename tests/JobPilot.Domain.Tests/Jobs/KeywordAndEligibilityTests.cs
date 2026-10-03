using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Tests.Jobs;

public class KeywordAndEligibilityTests
{
    private static readonly KeywordMatcher DotNet = new([".net", "c#", "dotnet", "asp.net", "blazor"], ["vb6"]);

    [Theory]
    [InlineData("Senior .NET Engineer", true)]
    [InlineData("Backend developer (C#, SQL)", true)]
    [InlineData("ASP.NET Core APIs", true)]
    [InlineData("Blazor Server front end", true)]
    [InlineData("We work on the planet's network", false)]
    [InlineData("C++ developer", false)]
    [InlineData("Maintain .NET and VB6 apps", false)]
    public void Matches_keywords_with_word_boundaries(string text, bool expected)
    {
        Assert.Equal(expected, DotNet.Matches(text));
    }

    [Fact]
    public void Without_keywords_everything_matches()
    {
        Assert.True(new KeywordMatcher([]).Matches("anything"));
    }

    [Theory]
    [InlineData("Worldwide", EligibilityStatus.Likely)]
    [InlineData("Anywhere in the World", EligibilityStatus.Likely)]
    [InlineData("LATAM (Remote)", EligibilityStatus.Likely)]
    [InlineData("Remote - International", EligibilityStatus.Likely)]
    [InlineData("Argentina, Brazil, Chile", EligibilityStatus.Likely)]
    [InlineData("Americas", EligibilityStatus.Likely)]
    [InlineData("Remote (US)", EligibilityStatus.Unlikely)]
    [InlineData("USA Only", EligibilityStatus.Unlikely)]
    [InlineData("Europe, UK", EligibilityStatus.Unlikely)]
    [InlineData("Mexico City", EligibilityStatus.Unlikely)]
    [InlineData("Remote", EligibilityStatus.Unknown)]
    [InlineData("", EligibilityStatus.Unknown)]
    [InlineData(null, EligibilityStatus.Unknown)]
    public void Guesses_eligibility_from_the_location_text(string? location, EligibilityStatus expected)
    {
        Assert.Equal(expected, EligibilityHeuristic.Assess(location, "Argentina").Status);
    }
}
