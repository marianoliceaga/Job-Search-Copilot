using Bunit;
using JobPilot.Web.Features.Transfer;
using JobPilot.Web.Infrastructure;
using JobPilot.Web.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace JobPilot.Web.Tests.Features.Transfer;

public sealed class TransferPanelTests : BunitContext, IAsyncLifetime
{
    private readonly TempDirectory _data = new();
    private TestDatabase? _database;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_database.Factory);
        Services.AddSingleton(new DataPaths(new JobPilotOptions { DataDirectory = _data.Path }));
        Services.AddSingleton(TimeProvider.System);
        Services.AddTransfer();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database!.DisposeAsync();
        _data.Dispose();
    }

    [Fact]
    public void Export_downloads_a_json_file_and_warns_that_it_is_not_encrypted()
    {
        var cut = Render<TransferPanel>();

        Assert.Contains("no está cifrado", System.Net.WebUtility.HtmlDecode(cut.Markup), StringComparison.Ordinal);
        cut.FindAll("button").First(b => b.TextContent.Contains("Exportar", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=export-done]")));
        var call = Assert.Single(JSInterop.Invocations, i => i.Identifier == "jobPilot.downloadFile");
        Assert.StartsWith("jobpilot-pc-", (string)call.Arguments[0]!, StringComparison.Ordinal);
        Assert.Equal(TransferService.ContentType, call.Arguments[1]);
    }
}
