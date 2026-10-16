using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Core.Infrastructure.Google;

public static class GoogleServiceCollectionExtensions
{
    /// <summary>
    /// Optional Google integration (phase A8): Gmail threads and drafts and the JobSearch Copilot calendar. The host registers
    /// <see cref="IGoogleAuthorizer"/> (Android's authorization API) and <see cref="IAppSigningInfo"/> (the installed app's certificate).
    /// </summary>
    public static IServiceCollection AddGoogle(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GoogleOptions>().Bind(configuration.GetSection(GoogleOptions.SectionName));
        services.AddHttpClient(GoogleAuthService.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<GoogleAuthService>();
        services.TryAddSingleton<IAppSigningInfo, UnknownAppSigningInfo>();
        services.AddHttpClient<GmailClient>((sp, http) =>
        {
            http.BaseAddress = sp.GetRequiredService<IOptions<GoogleOptions>>().Value.GmailBaseUrl;
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient<GoogleCalendarClient>((sp, http) =>
        {
            http.BaseAddress = sp.GetRequiredService<IOptions<GoogleOptions>>().Value.CalendarBaseUrl;
            http.Timeout = TimeSpan.FromSeconds(30);
        });
        return services;
    }
}
