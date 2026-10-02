namespace JobPilot.Web.Features.Pipeline;

public static class PipelineServiceCollectionExtensions
{
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        services.AddSingleton<PipelineSettingsStore>();
        services.AddSingleton<ApplicationStore>();
        services.AddSingleton<CalendarExport>();
        services.AddTransient<FollowUpHandler>();
        services.AddSingleton<Dashboard.DashboardService>();
        return services;
    }
}
