using JobPilot.Web.Features.Jobs.Analysis;
using JobPilot.Web.Features.Jobs.Sources;
using JobPilot.Web.Infrastructure.Persistence;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Features.Jobs;

public static class JobsServiceCollectionExtensions
{
    public static IServiceCollection AddJobs(this IServiceCollection services, IConfiguration configuration, string seedDirectory)
    {
        services.AddOptions<JobSourceOptions>().Bind(configuration.GetSection(JobSourceOptions.SectionName));

        // Registration order is the order in which "Buscar" reads the sources.
        AddSource<RemotiveSource>(services);
        AddSource<RemoteOkSource>(services);
        AddSource<HimalayasSource>(services);
        AddSource<JobicySource>(services);
        AddSource<WeWorkRemotelySource>(services);
        AddSource<GetOnBoardSource>(services);
        AddSource<WorkingNomadsSource>(services);
        AddSource<JobspressoSource>(services);
        AddSource<GreenhouseSource>(services);
        AddSource<LeverSource>(services);
        AddSource<AshbySource>(services);

        services.AddSingleton(new JobSeedFiles(seedDirectory));
        services.AddSingleton<SettingsDocumentStore>();
        services.AddSingleton<JobPreferencesStore>();
        services.AddSingleton<FollowedCompanyStore>();
        services.AddSingleton<JobIngestion>();
        services.AddSingleton<JobListService>();
        services.AddTransient<JobSearchService>();
        services.AddTransient<ManualJobImport>();
        services.AddTransient<JobAnalysisHandler>();
        services.AddSingleton<JobAnalysisReader>();
        return services;
    }

    private static void AddSource<TSource>(IServiceCollection services)
        where TSource : class, IJobSource
    {
        services.AddHttpClient<TSource>((sp, http) =>
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd(sp.GetRequiredService<IOptions<JobSourceOptions>>().Value.UserAgent);
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(options =>
            {
                // Sources rate-limit aggressively: one gentle retry at most.
                options.Retry.MaxRetryAttempts = 1;
                options.Retry.Delay = TimeSpan.FromSeconds(3);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(45);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(100);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(90);
            });
        services.AddTransient<IJobSource>(sp => sp.GetRequiredService<TSource>());
    }
}
