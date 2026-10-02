using JobPilot.Web.Features.Companies;

namespace JobPilot.Web.Features.Outreach;

public static class OutreachServiceCollectionExtensions
{
    /// <summary>Company profiles (phase 6) and outreach drafts. Nothing here sends messages.</summary>
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
