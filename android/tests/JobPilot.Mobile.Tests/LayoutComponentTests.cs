using Bunit;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Layout;
using JobPilot.Mobile.Core.Shared;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests;

public class LayoutComponentTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeNotificationScheduler _notifications = new();

    public LayoutComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<INotificationScheduler>(_notifications);
        Services.AddSingleton<JobPilot.Mobile.Core.Infrastructure.Google.IGoogleAuthorizer>(new FakeGoogleAuthorizer());
    }

    public async ValueTask InitializeAsync() => await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
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

    [Fact]
    public void The_layout_schedules_the_notifications_when_the_app_starts()
    {
        Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "contenido"))));

        // Off by default: the sync cancels whatever an earlier install or session left scheduled.
        Assert.Equal(1, _notifications.Replacements);
        Assert.Empty(_notifications.Scheduled!);
    }

    [Fact]
    public void A_tapped_notification_opens_its_application_page()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var requests = Services.GetRequiredService<AppNavigationRequests>();
        Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "contenido"))));
        var id = Guid.NewGuid();

        requests.Request($"/pipeline/{id}");

        Assert.Equal($"http://localhost/pipeline/{id}", navigation.Uri);
    }

    [Theory]
    [InlineData("/configuracion")]
    [InlineData("https://example.com/pipeline/00000000-0000-0000-0000-000000000000")]
    [InlineData("//example.com/pipeline/00000000-0000-0000-0000-000000000000")]
    [InlineData("/pipeline/not-a-guid")]
    [InlineData(null)]
    public void Only_application_pages_can_be_requested_from_outside_the_ui(string? route)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var requests = Services.GetRequiredService<AppNavigationRequests>();
        Render<MainLayout>(p => p.Add(c => c.Body, (RenderFragment)(b => b.AddContent(0, "contenido"))));

        requests.Request(route);

        Assert.False(AppNavigationRequests.IsAllowed(route));
        Assert.Null(requests.Take());
        Assert.Equal("http://localhost/", navigation.Uri);
    }
}
