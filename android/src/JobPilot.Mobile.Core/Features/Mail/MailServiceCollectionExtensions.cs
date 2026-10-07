namespace JobPilot.Mobile.Core.Features.Mail;

public static class MailServiceCollectionExtensions
{
    /// <summary>Phase A9: the web app's mail brief ("Hoy"), on demand and read-only.</summary>
    public static IServiceCollection AddMail(this IServiceCollection services)
    {
        services.AddSingleton<MailRulesSeed>();
        services.AddSingleton<MailRulesStore>();
        services.AddTransient<MailCheckService>();
        return services;
    }
}
