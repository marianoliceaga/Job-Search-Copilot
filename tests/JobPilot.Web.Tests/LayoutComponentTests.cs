using Bunit;
using JobPilot.Web.Components.Layout;
using JobPilot.Web.Components.Shared;
using MudBlazor.Services;

namespace JobPilot.Web.Tests;

public class LayoutComponentTests : BunitContext
{
    public LayoutComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void NavMenu_renders_one_link_per_section()
    {
        var cut = Render<NavMenu>();

        var hrefs = cut.FindAll("a").Select(a => a.GetAttribute("href")).ToList();

        Assert.Equal(AppSections.All.Count, hrefs.Count);
        Assert.All(AppSections.All, section => Assert.Contains(section.Href, hrefs));
    }

    [Fact]
    public void PendingSection_shows_title_and_delivery_phase()
    {
        var cut = Render<PendingSection>(p => p.Add(c => c.Section, AppSections.Profile));

        Assert.Equal(AppSections.Profile.Title, cut.Find("h1").TextContent);
        Assert.Contains($"fase {AppSections.Profile.Phase}", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Section_routes_are_unique()
    {
        Assert.Equal(AppSections.All.Count, AppSections.All.Select(s => s.Href).Distinct().Count());
    }
}
