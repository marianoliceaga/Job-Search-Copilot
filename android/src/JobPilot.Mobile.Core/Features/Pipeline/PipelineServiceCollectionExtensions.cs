using JobPilot.Mobile.Core.Infrastructure.Notifications;

namespace JobPilot.Mobile.Core.Features.Pipeline;

public static class PipelineServiceCollectionExtensions
{
    /// <summary>Pipeline, reminders, notifications and the Panel. The host registers <see cref="INotificationScheduler"/>.</summary>
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        services.AddSingleton<AppNavigationRequests>();
        services.AddSingleton<ReminderNotificationSettingsStore>();
        services.AddSingleton<ReminderNotificationSync>();
        services.AddSingleton<PipelineSettingsStore>();
        services.AddSingleton<ApplicationStore>();
        services.AddSingleton<CalendarExport>();
        services.AddTransient<FollowUpHandler>();
        services.AddTransient<ApplicationGoogleService>();
        services.AddSingleton<Dashboard.DashboardService>();
        return services;
    }
}
