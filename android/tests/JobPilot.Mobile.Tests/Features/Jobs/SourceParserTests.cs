using JobPilot.Domain.Jobs;
using JobPilot.Mobile.Core.Features.Jobs.Sources;
using JobPilot.Mobile.Core.Infrastructure.Text;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Tests.Features.Jobs;

public class SourceParserTests
{
    private static readonly IOptions<JobSourceOptions> Options = Microsoft.Extensions.Options.Options.Create(new JobSourceOptions());

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sources", name));

    [Fact]
    public void Remotive()
    {
        var jobs = RemotiveSource.Parse(Fixture("remotive.json"));

        Assert.Equal(2, jobs.Count);
        var job = jobs[0];
        Assert.Equal(("remotive", "Remotive", "900001"), (job.SourceKey, job.SourceName, job.SourceJobId));
        Assert.Equal("Senior .NET Engineer", job.Title);
        Assert.Equal("Northwind Payments", job.Company);
        Assert.Equal("https://remotive.com/remote-jobs/software-development/senior-net-engineer-900001", job.Url);
        Assert.Equal("Latin America", job.Location);
        Assert.Equal("$90k - $120k", job.SalaryText);
        Assert.Null(job.SalaryMin);
        Assert.Equal([".Net", "C#", "Azure"], job.Tags);
        Assert.Equal(new DateTime(2026, 9, 28, 10, 0, 0, DateTimeKind.Utc), job.PublishedAtUtc);
        Assert.Contains("- 5+ years of C#", job.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", job.Description, StringComparison.Ordinal);
        Assert.Null(jobs[1].SalaryText);
    }

    [Fact]
    public void RemoteOk_skips_the_legal_notice_and_reads_annual_usd_salaries()
    {
        var jobs = RemoteOkSource.Parse(Fixture("remoteok.json"));

        Assert.Equal(2, jobs.Count);
        var job = jobs[0];
        Assert.Equal(("Contoso Cloud", "Senior C# Developer", "Worldwide"), (job.Company, job.Title, job.Location));
        Assert.Equal((100_000m, 140_000m, "USD", SalaryPeriod.Year), (job.SalaryMin!.Value, job.SalaryMax!.Value, job.SalaryCurrency, job.SalaryPeriod!.Value));
        Assert.Equal("Build APIs in .NET 10.", job.Description.Split('\n').Last());
        Assert.Null(jobs[1].SalaryMin);
        Assert.Null(jobs[1].SalaryCurrency);
    }

    [Fact]
    public void Himalayas()
    {
        var jobs = HimalayasSource.Parse(Fixture("himalayas.json"));

        var job = jobs[0];
        Assert.Equal("Argentina, Brazil, Mexico", job.Location);
        Assert.Equal((6000m, 8000m, "USD", SalaryPeriod.Month), (job.SalaryMin!.Value, job.SalaryMax!.Value, job.SalaryCurrency, job.SalaryPeriod!.Value));
        Assert.Equal("https://himalayas.app/companies/fabrikam-health/jobs/blazor-developer", job.Url);
        Assert.Equal("Worldwide", jobs[1].Location);
        Assert.Null(jobs[1].SalaryCurrency);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790075996).UtcDateTime, job.PublishedAtUtc);
    }

    [Fact]
    public void Jobicy_decodes_entities_and_reads_salary()
    {
        var job = Assert.Single(JobicySource.Parse(Fixture("jobicy.json")));

        Assert.Equal("Senior ASP.NET Core Developer & Mentor", job.Title);
        Assert.Equal("LATAM", job.Location);
        Assert.Equal((85_000m, 110_000m, SalaryPeriod.Year), (job.SalaryMin!.Value, job.SalaryMax!.Value, job.SalaryPeriod!.Value));
        Assert.Equal("Work with Adventure Works on ASP.NET Core.", job.Description);
    }

    [Fact]
    public void WeWorkRemotely_splits_company_and_title_and_prefers_the_country_list()
    {
        var jobs = WeWorkRemotelySource.Parse(Fixture("weworkremotely.rss"));

        Assert.Equal(2, jobs.Count);
        Assert.Equal(("Litware", "Senior .NET Backend Engineer"), (jobs[0].Company, jobs[0].Title));
        Assert.Equal("Argentina, Bosnia and Herzegovina, Chile", jobs[0].Location);
        Assert.Equal(["C#", ".NET", "PostgreSQL"], jobs[0].Tags);
        Assert.Contains("We use .NET 10 and PostgreSQL.", jobs[0].Description, StringComparison.Ordinal);
        Assert.Equal("USA Only", jobs[1].Location);
    }

    [Fact]
    public void Greenhouse_decodes_the_escaped_html()
    {
        var jobs = new GreenhouseSource(new HttpClient(), Options).Parse(Fixture("greenhouse.json"), "Woodgrove Bank");

        var job = jobs[0];
        Assert.Equal("Greenhouse · Woodgrove Bank", job.SourceName);
        Assert.Equal(("Staff Software Engineer (.NET)", "Remote - LATAM"), (job.Title, job.Location));
        Assert.Equal("Lead our C# platform.\n\n- ASP.NET Core\n\n- Azure", job.Description);
        Assert.Equal(["Engineering"], job.Tags);
        Assert.Equal(new DateTime(2026, 9, 25, 13, 0, 0, DateTimeKind.Utc), job.PublishedAtUtc);
    }

    [Fact]
    public void Lever_combines_description_lists_and_salary_range()
    {
        var job = Assert.Single(new LeverSource(new HttpClient(), Options).Parse(Fixture("lever.json"), "Adventure Works"));

        Assert.Equal(("Senior Full Stack Engineer .NET / Angular", "Latin America (remote)", "Contractor"), (job.Title, job.Location, job.EmploymentType));
        Assert.Contains("Requirements", job.Description, StringComparison.Ordinal);
        Assert.Contains("- 7+ years of C#", job.Description, StringComparison.Ordinal);
        Assert.Equal((5000m, 7000m, SalaryPeriod.Month), (job.SalaryMin!.Value, job.SalaryMax!.Value, job.SalaryPeriod!.Value));
        Assert.Equal("https://jobs.lever.co/AdventureWorks/11111111-2222-3333-4444-555555555555/apply", job.ApplyUrl);
    }

    [Fact]
    public void Ashby_reads_salary_component_and_skips_unlisted_jobs()
    {
        var job = Assert.Single(new AshbySource(new HttpClient(), Options).Parse(Fixture("ashby.json"), "Contoso"));

        Assert.Equal("LATAM (Remote) · Argentina", job.Location);
        Assert.Equal((120_000m, 150_000m, "USD", SalaryPeriod.Year), (job.SalaryMin!.Value, job.SalaryMax!.Value, job.SalaryCurrency, job.SalaryPeriod!.Value));
        Assert.Equal("$120K – $150K • Offers Equity", job.SalaryText);
    }

    [Fact]
    public void Html_is_converted_to_plain_text_without_scripts()
    {
        var text = HtmlText.ToPlainText("<h2>Role</h2><p>Hello&nbsp;<b>world</b><br>next</p><ul><li>One</li><li>Two</li></ul><style>p{}</style><img src=x onerror=alert(1)>");

        Assert.Equal("Role\n\nHello world\nnext\n\n- One\n\n- Two", text);
    }
}
