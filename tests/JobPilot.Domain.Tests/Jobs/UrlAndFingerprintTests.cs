using JobPilot.Domain.Jobs;

namespace JobPilot.Domain.Tests.Jobs;

public class UrlAndFingerprintTests
{
    [Theory]
    [InlineData("https://www.Remotive.com/remote-jobs/dev/senior-123/?utm_source=x&ref=y", "https://remotive.com/remote-jobs/dev/senior-123")]
    [InlineData("https://job-boards.greenhouse.io/current/jobs/8687284002?gh_src=my.greenhouse.search", "https://job-boards.greenhouse.io/current/jobs/8687284002")]
    [InlineData("https://jobs.ashbyhq.com/1password/c6933be3-80a6-41f0-8e37-e70f72cae134/application?lever-origin=applied&lever-source%5B%5D=nodesk", "https://jobs.ashbyhq.com/1password/c6933be3-80a6-41f0-8e37-e70f72cae134")]
    [InlineData("https://remoteOK.com/remote-jobs/123#apply", "https://remoteok.com/remote-jobs/123")]
    [InlineData("https://example.com/job?id=5&utm_medium=a", "https://example.com/job?id=5")]
    public void Canonical_urls_drop_tracking_and_presentation_details(string url, string expected)
    {
        Assert.Equal(expected, UrlNormalizer.Canonicalize(url));
    }

    [Fact]
    public void The_same_offer_has_the_same_fingerprint_across_sources()
    {
        var a = JobFingerprint.Compute("Lemon.io", "Senior .NET Developer (Remote)");
        var b = JobFingerprint.Compute("lemon.io Inc.", "Senior .NET developer - Remote");
        var c = JobFingerprint.Compute("CI&T", "[Job - 30532] Senior .NET Developer");
        var d = JobFingerprint.Compute("CI&T", "Senior .NET Developer");

        Assert.Equal(a, b);
        Assert.Equal(c, d);
        Assert.NotEqual(a, JobFingerprint.Compute("Lemon.io", "Senior Java Developer"));
    }
}
