using System.Net;
using JobPilot.Web.Components.Layout;
using JobPilot.Web.Tests.TestSupport;

namespace JobPilot.Web.Tests;

public class AppSmokeTests(JobPilotWebFactory factory) : IClassFixture<JobPilotWebFactory>
{
    public static TheoryData<string, string> Sections()
    {
        var data = new TheoryData<string, string>();
        foreach (var section in AppSections.All)
        {
            data.Add(section.Href, section.Title);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Sections))]
    public async Task Every_section_renders_with_its_title(string href, string title)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(href, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(title, WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_application_shows_a_message()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync($"/pipeline/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Contains("Postulación no encontrada", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_route_returns_not_found()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/no-existe", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
