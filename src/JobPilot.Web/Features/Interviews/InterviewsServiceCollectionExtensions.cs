using JobPilot.Web.Features.Negotiation;

namespace JobPilot.Web.Features.Interviews;

public static class InterviewsServiceCollectionExtensions
{
    /// <summary>Interview preparation, practice conversations and negotiation (phase 7).</summary>
    public static IServiceCollection AddInterviewsAndNegotiation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PracticeOptions>().Bind(configuration.GetSection(PracticeOptions.SectionName));
        services.AddSingleton<InterviewPrepStore>();
        services.AddTransient<InterviewQuestionsHandler>();
        services.AddTransient<StarStoriesHandler>();
        services.AddSingleton<PracticeStore>();
        services.AddTransient<PracticeHandler>();
        services.AddSingleton<OfferStore>();
        services.AddTransient<NegotiationScriptHandler>();
        return services;
    }
}
