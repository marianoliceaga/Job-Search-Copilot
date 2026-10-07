using JobPilot.Mobile.Core.Features.Jobs;
using JobPilot.Mobile.Core.Features.Pipeline;
using JobPilot.Mobile.Core.Features.Profile;
using JobPilot.Mobile.Core.Features.Profile.Import;
using JobPilot.Mobile.Core.Features.Settings;
using JobPilot.Mobile.Core.Features.Tailoring;
using JobPilot.Mobile.Core.Infrastructure;
using JobPilot.Mobile.Core.Infrastructure.Llm;
using JobPilot.Mobile.Core.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobPilot.Mobile.Core;

public static class MobileServiceCollectionExtensions
{
    /// <summary>
    /// The app's own services. The host adds the platform and UI ones: <see cref="Infrastructure.Secrets.ISecretStore"/>,
    /// <see cref="Infrastructure.Files.IFilePickerService"/>, <see cref="Infrastructure.Files.IShareService"/>,
    /// <see cref="Infrastructure.Notifications.INotificationScheduler"/>, the BlazorWebView and MudBlazor.
    /// </summary>
    public static IServiceCollection AddJobPilotMobile(this IServiceCollection services, IConfiguration configuration, MobilePaths paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton<AppInitializer>();
        services.AddSingleton(TimeProvider.System);
        services.AddMobilePersistence(paths);
        services.AddSingleton<SettingsDocumentStore>();
        services.AddMobileLlm(configuration);
        services.AddTransient<ConnectionTestHandler>();
        services.AddSingleton<ProfileSeed>();
        services.AddSingleton<ProfileStore>();
        services.AddSingleton<MasterCvStore>();
        services.AddTransient<CvImportHandler>();
        services.AddJobs(configuration);
        services.AddTailoring();
        services.AddPipeline();
        return services;
    }
}
