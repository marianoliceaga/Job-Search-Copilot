using System.Net;
using Bunit;
using JobPilot.Web.Features.Settings;
using JobPilot.Web.Infrastructure;
using JobPilot.Web.Infrastructure.Google;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Web.Tests.Google;

/// <summary>The setup wizard's OAuth client: entered in the app, stored encrypted, used when user-secrets have none.</summary>
public sealed class GoogleSetupTests : BunitContext
{
    private const string ClientId = "123-wizard.apps.googleusercontent.com";

    private readonly TempDirectory _data = new();
    private readonly FakeGoogleHandler _google = new();

    private void AddServices(bool configured = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        var settings = new Dictionary<string, string?>
        {
            ["Google:AuthorizationEndpoint"] = "https://accounts.google.test/o/oauth2/v2/auth",
            ["Google:TokenEndpoint"] = "https://oauth2.google.test/token",
        };
        if (configured)
        {
            settings["Google:ClientId"] = "from-config.apps.googleusercontent.com";
            settings["Google:ClientSecret"] = "config-secret";
        }

        Services.AddSingleton(new DataPaths(new JobPilotOptions { DataDirectory = _data.Path }));
        Services.AddSingleton(new JobPilotOptions { Port = 5080 });
        Services.AddSingleton(TimeProvider.System);
        Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        Services.AddGoogle(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        Services.ConfigureHttpClientDefaults(builder => builder.ConfigurePrimaryHttpMessageHandler(() => _google));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _data.Dispose();
    }

    [Theory]
    [InlineData("", "secret", "Pegá el Client ID")]
    [InlineData("client secret.apps.googleusercontent.com", "secret", "no llevan espacios")]
    [InlineData("looks-like-a-secret", "secret", "termina en")]
    public void The_client_store_rejects_incomplete_or_swapped_values(string id, string secret, string expected)
    {
        AddServices();
        var store = Services.GetRequiredService<GoogleClientStore>();

        Assert.Contains(expected, store.Save(id, secret), StringComparison.Ordinal);
        Assert.Null(store.Read());
    }

    [Fact]
    public void The_client_is_stored_encrypted_and_can_be_deleted()
    {
        AddServices();
        var store = Services.GetRequiredService<GoogleClientStore>();

        Assert.Null(store.Save($"  {ClientId} ", " wizard-secret "));

        Assert.Equal((ClientId, "wizard-secret"), (store.Read()!.ClientId, store.Read()!.ClientSecret));
        Assert.DoesNotContain("wizard-secret", File.ReadAllText(store.FilePath), StringComparison.Ordinal);
        store.Delete();
        Assert.Null(store.Read());
    }

    [Fact]
    public async Task The_auth_flow_uses_the_wizard_client_when_configuration_has_none()
    {
        AddServices();
        var auth = Services.GetRequiredService<GoogleAuthService>();
        Assert.False(auth.Status().Configured);
        Services.GetRequiredService<GoogleClientStore>().Save(ClientId, "wizard-secret");
        _google.On("POST", "/token", HttpStatusCode.OK, new { access_token = "access-1", expires_in = 3600, refresh_token = "refresh-1", scope = GoogleScopes.CalendarAppCreated });

        var status = auth.Status();
        var url = new Uri(auth.BuildAuthorizationUrl());
        var result = await auth.CompleteAsync("code", System.Web.HttpUtility.ParseQueryString(url.Query)["state"], null, Xunit.TestContext.Current.CancellationToken);

        Assert.Equal((true, GoogleClientSource.App), (status.Configured, status.ClientSource));
        Assert.Equal(ClientId, System.Web.HttpUtility.ParseQueryString(url.Query)["client_id"]);
        Assert.True(result.IsSuccess, result.Error);
        var token = Assert.Single(_google.Requests);
        Assert.Contains("client_secret=wizard-secret", token.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void User_secrets_win_over_the_wizard_client()
    {
        AddServices(configured: true);
        Services.GetRequiredService<GoogleClientStore>().Save(ClientId, "wizard-secret");
        var auth = Services.GetRequiredService<GoogleAuthService>();

        var url = new Uri(auth.BuildAuthorizationUrl());

        Assert.Equal(GoogleClientSource.Configuration, auth.Status().ClientSource);
        Assert.Equal("from-config.apps.googleusercontent.com", System.Web.HttpUtility.ParseQueryString(url.Query)["client_id"]);
    }

    [Fact]
    public async Task A_rejected_client_is_reported_in_plain_words()
    {
        AddServices();
        var auth = Services.GetRequiredService<GoogleAuthService>();
        Services.GetRequiredService<GoogleClientStore>().Save(ClientId, "wrong-secret");
        _google.On("POST", "/token", HttpStatusCode.Unauthorized, new { error = "invalid_client" });

        var url = new Uri(auth.BuildAuthorizationUrl());
        var result = await auth.CompleteAsync("code", System.Web.HttpUtility.ParseQueryString(url.Query)["state"], null, Xunit.TestContext.Current.CancellationToken);

        Assert.Contains("no reconoce el cliente OAuth", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_wizard_walks_through_google_cloud_and_saves_the_client()
    {
        AddServices();
        var cut = Render<GoogleSetupWizard>();

        Assert.Contains("jp-step-current", cut.Find("[data-testid=google-step-1]").ClassName, StringComparison.Ordinal);
        for (var i = 0; i < 3; i++)
        {
            cut.Find("[data-testid=google-step-next]").Click();
        }

        // Step 4: the redirect URI to paste in Google Cloud, with a copy button.
        Assert.Equal("http://localhost:5080/google/callback", cut.Find("[data-testid=google-redirect-uri]").TextContent);
        cut.FindAll("[data-testid=google-step-4] button").Single(b => b.TextContent.Contains("Copiar", StringComparison.Ordinal)).Click();
        Assert.Equal("http://localhost:5080/google/callback", Assert.Single(JSInterop.Invocations, i => i.Identifier == "jobPilot.copyText").Arguments[0]);
        cut.Find("[data-testid=google-step-next]").Click();

        cut.Find("input[data-testid=google-client-id]").Input("secret-pasted-in-the-id-field");
        cut.Find("input[data-testid=google-client-secret]").Input("wizard-secret");
        cut.Find("[data-testid=google-client-save]").Click();
        Assert.Contains("termina en", cut.Find("[data-testid=google-client-error]").TextContent, StringComparison.Ordinal);

        cut.Find("input[data-testid=google-client-id]").Input(ClientId);
        cut.Find("[data-testid=google-client-save]").Click();

        Assert.Equal(ClientId, Services.GetRequiredService<GoogleClientStore>().Read()!.ClientId);
        Assert.Contains("jp-step-current", cut.Find("[data-testid=google-step-6]").ClassName, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=google-connect]"));
    }

    [Fact]
    public void With_the_client_in_user_secrets_the_wizard_opens_on_the_connect_step()
    {
        AddServices(configured: true);
        var cut = Render<GoogleSetupWizard>();

        Assert.Contains("jp-step-current", cut.Find("[data-testid=google-step-6]").ClassName, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("[data-testid=google-connect]"));
        cut.Find("[data-testid=google-step-5] button").Click();
        Assert.NotNull(cut.Find("[data-testid=google-client-config]"));
    }
}
