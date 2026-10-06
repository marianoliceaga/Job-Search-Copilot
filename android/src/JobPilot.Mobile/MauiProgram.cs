using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Services;
using MudBlazor.Services;

namespace JobPilot.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();
        // Data in the app's private folder (removed when the app is uninstalled); secrets in Android's secure storage.
        builder.Services.AddJobPilotMobile(MobileConfiguration.Load(), new MobilePaths(FileSystem.AppDataDirectory));
        builder.Services.AddSingleton<ISecretStore, MauiSecretStore>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}
