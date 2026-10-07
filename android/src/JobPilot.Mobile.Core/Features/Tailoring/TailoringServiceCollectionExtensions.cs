using JobPilot.Mobile.Core.Features.Tailoring.Export;

namespace JobPilot.Mobile.Core.Features.Tailoring;

public static class TailoringServiceCollectionExtensions
{
    public static IServiceCollection AddTailoring(this IServiceCollection services)
    {
        services.AddSingleton<TechnologyCatalog>();
        services.AddSingleton<TailoringContextFactory>();
        services.AddSingleton<TailoredDocumentStore>();
        services.AddTransient<TailoringHandler>();
        services.AddSingleton<DocumentExporter>();
        return services;
    }
}
