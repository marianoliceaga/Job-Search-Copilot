using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Tests.Jobs;

public class AtsUrlParserTests
{
    [Theory]
    [InlineData("https://job-boards.greenhouse.io/current/jobs/8687284002?gh_src=x", AtsKind.Greenhouse, "current", "8687284002")]
    [InlineData("https://boards.greenhouse.io/praxent", AtsKind.Greenhouse, "praxent", null)]
    [InlineData("https://boards.greenhouse.io/embed/job_app?for=current&token=8687284002", AtsKind.Greenhouse, "current", "8687284002")]
    [InlineData("https://jobs.lever.co/Ubiminds/38981a67-28d7-4789-b74a-bb00d44fba6b/apply", AtsKind.Lever, "Ubiminds", "38981a67-28d7-4789-b74a-bb00d44fba6b")]
    [InlineData("https://jobs.ashbyhq.com/agility.io/2320547b-218a-4730-8c43-b55a85819c6b", AtsKind.Ashby, "agility.io", "2320547b-218a-4730-8c43-b55a85819c6b")]
    [InlineData("https://jobs.ashbyhq.com/1password", AtsKind.Ashby, "1password", null)]
    public void Recognises_public_ats_urls(string url, AtsKind ats, string board, string? jobId)
    {
        Assert.Equal(new AtsReference(ats, board, jobId), AtsUrlParser.Parse(url));
    }

    [Theory]
    [InlineData("https://www.linkedin.com/jobs/view/123")]
    [InlineData("https://remotive.com/remote-jobs/x")]
    [InlineData("not a url")]
    [InlineData(null)]
    public void Ignores_other_urls(string? url)
    {
        Assert.Null(AtsUrlParser.Parse(url));
    }
}
