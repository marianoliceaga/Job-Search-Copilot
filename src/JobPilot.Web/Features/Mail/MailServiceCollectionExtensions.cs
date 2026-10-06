namespace JobPilot.Web.Features.Mail;

public static class MailServiceCollectionExtensions
{
    /// <summary>Phase 9: mail inbox and daily brief (ported from JobSearchDesk), on demand and read-only.</summary>
    public static IServiceCollection AddMail(this IServiceCollection services, string seedDirectory)
    {
        services.AddSingleton(new MailRulesSeed(Path.Combine(seedDirectory, "mail-rules.seed.json")));
        services.AddSingleton<MailRulesStore>();
        services.AddTransient<MailCheckService>();
        return services;
    }
}
