using JobPilot.Desktop.Core;

namespace JobPilot.Desktop.Tests;

public sealed class NavigationPolicyTests
{
    private static readonly Uri App = new("http://localhost:5080/");

    [Theory]
    [InlineData("http://localhost:5080/", NavigationAction.Allow)]
    [InlineData("http://localhost:5080/ofertas/3f2a?x=1", NavigationAction.Allow)]
    [InlineData("http://127.0.0.1:5080/pipeline", NavigationAction.Allow)]
    [InlineData("http://[::1]:5080/perfil", NavigationAction.Allow)]
    [InlineData("about:blank", NavigationAction.Allow)]
    [InlineData("blob:http://localhost:5080/7d1c0b3e-5f0a-4b8e-9f3e-2c1d4e5f6a7b", NavigationAction.Allow)]
    [InlineData("http://localhost:5080/google/connect", NavigationAction.OpenInBrowser)]
    [InlineData("http://localhost:5080/google/connect?alcance=calendario", NavigationAction.OpenInBrowser)]
    [InlineData("http://localhost:5080/Google/Connect", NavigationAction.OpenInBrowser)]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=x", NavigationAction.OpenInBrowser)]
    [InlineData("https://boards.greenhouse.io/contoso/jobs/1", NavigationAction.OpenInBrowser)]
    [InlineData("https://localhost:5080/", NavigationAction.OpenInBrowser)]
    [InlineData("http://localhost:5081/", NavigationAction.OpenInBrowser)]
    [InlineData("http://localhost.contoso.example/", NavigationAction.OpenInBrowser)]
    [InlineData("file:///C:/Windows/System32/", NavigationAction.Block)]
    [InlineData("javascript:alert(1)", NavigationAction.Block)]
    [InlineData("data:text/html,hello", NavigationAction.Block)]
    [InlineData("ms-settings:privacy", NavigationAction.Block)]
    [InlineData("/relative", NavigationAction.Block)]
    [InlineData("", NavigationAction.Block)]
    [InlineData(null, NavigationAction.Block)]
    public void Only_the_app_stays_in_the_window(string? target, NavigationAction expected) =>
        Assert.Equal(expected, NavigationPolicy.Decide(target, App));

    [Fact]
    public void The_google_connection_is_recognized_with_or_without_the_calendar_only_option()
    {
        Assert.True(NavigationPolicy.IsGoogleConnect(new Uri("http://localhost:5080/google/connect?alcance=calendario")));
        Assert.False(NavigationPolicy.IsGoogleConnect(new Uri("http://localhost:5080/google/callback?code=x")));
        Assert.False(NavigationPolicy.IsGoogleConnect(new Uri("http://localhost:5080/configuracion?seccion=google")));
    }
}
