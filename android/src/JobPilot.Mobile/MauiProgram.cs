using JobPilot.Mobile.Core;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Files;
using JobPilot.Mobile.Core.Infrastructure.Google;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Platforms.Android.Google;
using JobPilot.Mobile.Platforms.Android.Notifications;
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
        builder.Services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        builder.Services.AddSingleton<IShareService, MauiShareService>();
        builder.Services.AddSingleton<INotificationScheduler, AndroidNotificationScheduler>();
        builder.Services.AddSingleton<IGoogleAuthorizer, AndroidGoogleAuthorizer>();
        // Exports shared in an earlier session are not kept.
        MauiShareService.ClearSharedFiles();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        return builder.Build();
    }
}
