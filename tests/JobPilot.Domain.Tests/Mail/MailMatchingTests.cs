using JobPilot.Domain.Mail;

namespace JobPilot.Domain.Tests.Mail;

public class MailMatchingTests
{
    [Theory]
    [InlineData("Entrevista técnica", true)]
    [InlineData("ENTREVISTAS", false)]
    [InlineData("Coordinemos la entrevista mañana", true)]
    public void Plain_terms_match_whole_words_ignoring_case_and_accents(string text, bool expected)
    {
        var terms = TermSet.Compile(["entrevista"], "test");

        Assert.Equal(expected, terms.IsMatch(MailText.Normalize(text)));
    }

    [Fact]
    public void Patterns_sets_and_empty_sets_behave_as_documented()
    {
        var sets = new SetResolver(new Dictionary<string, List<string>> { ["dotnet"] = [@"re:(?<![\p{L}\p{N}])\.net\b", "c#"] });
        var terms = TermSet.Compile(sets.Expand(["$dotnet"], "test"), "test");

        Assert.True(terms.IsMatch(MailText.Normalize("Senior .NET Engineer")));
        Assert.False(terms.IsMatch(MailText.Normalize("Visit example.net today")));
        Assert.False(TermSet.Nothing.IsMatch("anything"));
        Assert.Throws<MailRulesException>(() => sets.Expand(["$missing"], "waitingRules[0]"));
        Assert.Throws<MailRulesException>(() => TermSet.Compile(["re:(unclosed"], "test"));
    }

    [Theory]
    [InlineData("noreply@talent.example.com", true)]
    [InlineData("jobs@example.com", true)]
    [InlineData("alerts@example.net", true)]
    [InlineData("ana@example.org", false)]
    public void Sender_sets_match_addresses_domains_with_subdomains_and_patterns(string address, bool expected)
    {
        var senders = SenderSet.Compile(["jobs@example.com", "talent.example.com", "re:^alerts@"], "test");

        Assert.Equal(expected, senders.IsMatch(address));
    }

    [Fact]
    public void Templates_fill_values_counts_and_keep_unknown_placeholders()
    {
        var text = MailTemplate.Fill("{n|hilo|hilos} de {sender} {typo}", new Dictionary<string, string> { ["n"] = "3", ["sender"] = "Ana" });

        Assert.Equal("3 hilos de Ana {typo}", text);
    }

    [Fact]
    public void The_window_reads_since_the_last_check_capped_and_one_day_the_first_time()
    {
        var now = MailTestData.Now;

        Assert.Equal("newer_than:1d", MailWindow.Compute(now, null, 4).QueryFragment);
        var recent = MailWindow.Compute(now, now.AddHours(-30), 4);
        Assert.Equal(now.AddHours(-30), recent.Since);
        Assert.Equal($"in:inbox after:{now.AddHours(-30).ToUnixTimeSeconds()}", recent.Apply("in:inbox {since}"));
        Assert.Equal(now.AddDays(-4), MailWindow.Compute(now, now.AddDays(-10), 4).Since);
    }
}
