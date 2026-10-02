using JobPilot.Web.Features.Tailoring.Export;

namespace JobPilot.Web.Features.Tailoring;

public static class TailoringServiceCollectionExtensions
{
    public static IServiceCollection AddTailoring(this IServiceCollection services, string seedDirectory)
    {
        services.AddSingleton(new TechnologyCatalog(Path.Combine(seedDirectory, "technology-terms.txt")));
        services.AddSingleton<TailoringContextFactory>();
        services.AddSingleton<TailoredDocumentStore>();
        services.AddTransient<TailoringHandler>();
        services.AddSingleton<DocumentExporter>();
        return services;
    }
}
