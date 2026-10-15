using Microsoft.Extensions.Options;

namespace JobPilot.Web.Infrastructure.Google;

public static class GoogleServiceCollectionExtensions
{
    /// <summary>Optional Google integration (phase 8): OAuth, Gmail drafts/threads and the JobPilot calendar.</summary>
    public static IServiceCollection AddGoogle(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GoogleOptions>().Bind(configuration.GetSection(GoogleOptions.SectionName));
        services.AddHttpClient(GoogleAuthService.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<GoogleTokenStore>();
        services.AddSingleton<GoogleClientStore>();
        services.AddSingleton<GoogleAuthService>();
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

    /// <summary>OAuth endpoints: start the consent flow and receive Google's redirect.</summary>
    public static IEndpointRouteBuilder MapGoogleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ?alcance=calendario asks only for the JobPilot calendar (accounts where Gmail access is blocked).
        endpoints.MapGet("/google/connect", (string? alcance, GoogleAuthService auth) =>
        {
            var access = alcance == "calendario" ? GoogleAccess.CalendarOnly : GoogleAccess.Full;
            try
            {
                return Results.Redirect(auth.BuildAuthorizationUrl(access));
            }
            catch (InvalidOperationException)
            {
                return Results.Redirect("/configuracion?google=no-configurado");
            }
        });

        endpoints.MapGet(GoogleOptions.CallbackPath, async (string? code, string? state, string? error, GoogleAuthService auth, GmailClient gmail, CancellationToken cancellationToken) =>
        {
            var result = await auth.CompleteAsync(code, state, error, cancellationToken);
            if (!result.IsSuccess)
            {
                return Results.Redirect("/configuracion?google=error&mensaje=" + Uri.EscapeDataString(result.Error!));
            }

            if (result.Value!.CanReadMail && await gmail.ProfileEmailAsync(cancellationToken) is { Value: { } email })
            {
                auth.SetEmail(email);
            }

            return Results.Redirect("/configuracion?google=conectado");
        });
        return endpoints;
    }
}
