using JobPilot.Domain.Mail;

namespace JobPilot.Domain.Tests.Mail;

public class MailApplicationMatcherTests
{
    private static readonly MailApplicationCandidate Contoso = new(Guid.NewGuid(), "Senior .NET Engineer", "Contoso, Inc.", []);
    private static readonly MailApplicationCandidate Northwind = new(Guid.NewGuid(), "Backend Developer", "Northwind Traders", ["ana@example.com"]);
    private static readonly MailApplicationCandidate Ab = new(Guid.NewGuid(), "Developer", "AB", []);

    [Fact]
    public void A_contact_address_is_the_strongest_evidence()
    {
        var matches = MailApplicationMatcher.Match("ANA@example.com", "Ana", "Contoso interview", [Contoso, Northwind]);

        Assert.Equal([Northwind], matches);
    }

    [Theory]
    [InlineData("noreply@contoso.example.com", "", "Hello")]
    [InlineData("jobs-noreply@northwindtraders.example.com", "", "Hello")]
    [InlineData("recruiter@example.org", "", "Your Contoso interview")]
    [InlineData("inmail-noreply@linkedin.com", "Ana from Northwind Traders", "Ana sent you a message")]
    public void The_company_is_found_in_the_sender_domain_subject_or_name(string address, string name, string subject)
    {
        var matches = MailApplicationMatcher.Match(address, name, subject, [Contoso, Northwind]);

        Assert.Single(matches);
    }

    [Fact]
    public void Short_or_absent_company_names_do_not_match_by_accident()
    {
        Assert.Empty(MailApplicationMatcher.Match("ab@example.com", "", "About the role", [Ab, Contoso]));
        Assert.Empty(MailApplicationMatcher.Match("ana@example.org", "Ana", "Coffee?", [Contoso, Northwind]));
    }
}
