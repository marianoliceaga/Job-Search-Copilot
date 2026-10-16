using Bunit;
using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Features.Settings;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Mobile.Tests.Features.Transfer;

/// <summary>Configuración → Exportar / Importar on the phone: the file leaves through the share sheet and comes back through the picker.</summary>
public sealed class TransferPanelTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _directory = new();
    private readonly FakeShareService _share = new();
    private readonly FakeFilePicker _picker = new();

    public TransferPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddLogging();
        Services.AddMudServices();
        Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(_directory.Path));
        Services.AddSingleton<ISecretStore>(new InMemorySecretStore());
        Services.AddSingleton<INotificationScheduler>(new FakeNotificationScheduler());
        Services.AddSingleton<IGoogleAuthorizer>(new FakeGoogleAuthorizer());
        Services.AddSingleton<IFilePickerService>(_picker);
        Services.AddSingleton<IShareService>(_share);
    }

    public async ValueTask InitializeAsync() => await Services.GetRequiredService<AppInitializer>().EnsureInitializedAsync();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _directory.Dispose();
    }

    [Fact]
    public void Export_shares_the_file_and_an_import_shows_what_would_change_before_combining()
    {
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/configuracion?seccion=datos");
        var cut = Render<SettingsPage>();

        Assert.Contains("no está cifrado", System.Net.WebUtility.HtmlDecode(cut.Markup), StringComparison.Ordinal);
        Button(cut, "Exportar y compartir").Click();
        cut.WaitForAssertion(() => Assert.Single(_share.Shared));
        var (_, fileName, contentType, content) = _share.Shared[0];
        Assert.StartsWith("jobsearch-copilot-celular-", fileName, StringComparison.Ordinal);
        Assert.Equal("application/json", contentType);

        // A PC file with one followed company the phone does not have.
        var file = System.Text.Json.Nodes.JsonNode.Parse(content)!.AsObject();
        file["app"] = "web";
        file["tables"]!["FollowedCompanies"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(
            """{"Id":900,"AddedAtUtc":"2026-10-07 10:00:00","Ats":"Lever","BoardToken":"contoso-example","Name":"Contoso","Notes":null}"""));
        var json = file.ToJsonString();
        _picker.Next = new PickResult(new PickedFile("jobpilot-pc.json", System.Text.Encoding.UTF8.GetBytes(json)));
        Button(cut, "Elegir archivo").Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=transfer-preview]")));
        Assert.Equal([".json"], _picker.Requests.Single().Extensions);
        var preview = System.Net.WebUtility.HtmlDecode(cut.Find("[data-testid=transfer-preview]").TextContent);
        Assert.Contains("Archivo de la PC", preview, StringComparison.Ordinal);
        Assert.Contains("Empresas seguidas", preview, StringComparison.Ordinal);
        Assert.Contains("agrega 1", preview, StringComparison.Ordinal);

        Button(cut, "Combinar").Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=transfer-done]")));
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<SettingsPage> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal));
}
