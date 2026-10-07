using JobPilot.Mobile.Core.Features.Companies;

namespace JobPilot.Mobile.Core.Features.Outreach;

public static class OutreachServiceCollectionExtensions
{
    /// <summary>Company profiles and outreach drafts (phase A6), with the web app's settings. Nothing here sends messages.</summary>
    public static IServiceCollection AddCompaniesAndOutreach(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CompanyOptions>().Bind(configuration.GetSection(CompanyOptions.SectionName));
        services.AddOptions<OutreachOptions>().Bind(configuration.GetSection(OutreachOptions.SectionName));
        services.AddSingleton<CompanyStore>();
        services.AddTransient<CompanyResearchHandler>();
        services.AddTransient<OutreachHandler>();
        services.AddSingleton<OutreachStore>();
        services.AddTransient<LinkedInReviewHandler>();
        return services;
    }
}
