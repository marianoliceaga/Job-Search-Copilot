using JobPilot.Mobile.Core.Features.Negotiation;

namespace JobPilot.Mobile.Core.Features.Interviews;

public static class InterviewsServiceCollectionExtensions
{
    /// <summary>Interview preparation, practice conversations and negotiation (phase A7), with the web app's settings.</summary>
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
