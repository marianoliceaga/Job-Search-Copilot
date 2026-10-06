using Bunit;
using JobPilot.Mobile.Core.Layout;
using JobPilot.Mobile.Core.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests;

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
    public void PendingSection_shows_title_and_android_phase()
    {
        var cut = Render<PendingSection>(p => p.Add(c => c.Section, AppSections.Profile));

        Assert.Equal(AppSections.Profile.Title, cut.Find("h1").TextContent);
        Assert.Contains($"fase {AppSections.Profile.Phase}", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Section_routes_are_unique_and_match_the_web_app()
    {
        Assert.Equal(AppSections.All.Count, AppSections.All.Select(s => s.Href).Distinct().Count());
        Assert.Equal(
            ["/", "/perfil", "/ofertas", "/pipeline", "/empresas", "/outreach", "/entrevistas", "/negociacion", "/configuracion"],
            AppSections.All.Select(s => s.Href));
    }

    [Fact]
    public void Every_section_has_a_page()
    {
        var routes = typeof(AppSections).Assembly.GetTypes()
            .SelectMany(t => t.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>())
            .Select(r => r.Template)
            .ToList();

        Assert.All(AppSections.All, section => Assert.Contains(section.Href, routes));
    }

    [Fact]
    public void The_drawer_closes_after_navigating()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "contenido"))));

        cut.Find("button[aria-label='Mostrar u ocultar el menú']").Click();
        Assert.Contains("mud-drawer--open", cut.Find("[data-testid=drawer]").ClassName, StringComparison.Ordinal);

        navigation.NavigateTo("/perfil");

        cut.WaitForAssertion(() => Assert.DoesNotContain("mud-drawer--open", cut.Find("[data-testid=drawer]").ClassName, StringComparison.Ordinal));
    }
}
