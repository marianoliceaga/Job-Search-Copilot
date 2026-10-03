using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace JobPilot.Web.Infrastructure.Google;

public sealed record GoogleStatus(bool Configured, bool Connected, string? Email, IReadOnlyList<string> GrantedScopes, DateTime? ConnectedAtUtc)
{
    public bool CanReadMail => GrantedScopes.Contains(GoogleScopes.GmailReadonly);

    public bool CanCreateDrafts => GrantedScopes.Contains(GoogleScopes.GmailCompose);

    public bool CanUseCalendar => GrantedScopes.Contains(GoogleScopes.CalendarAppCreated);

    public IReadOnlyList<string> MissingScopes => GoogleScopes.All.Except(GrantedScopes).ToList();
}

/// <summary>A Google API outcome: never throws for provider errors; <see cref="Error"/> is user-facing (Spanish).</summary>
public sealed record GoogleResult<T>(T? Value, string? Error)
{
    public bool IsSuccess => Error is null;

    public static GoogleResult<T> Ok(T value) => new(value, null);

    public static GoogleResult<T> Fail(string error) => new(default, error);
}

/// <summary>
/// OAuth 2.0 for a local web app (Google "Web application" client, redirect <c>http://localhost:{port}/google/callback</c>):
/// state against CSRF, offline access for a refresh token, granted scopes checked, automatic refresh and revocation.
/// </summary>
public sealed class GoogleAuthService(
    IHttpClientFactory httpFactory,
    IOptionsMonitor<GoogleOptions> options,
    GoogleTokenStore store,
    JobPilotOptions jobPilot,
    TimeProvider time)
{
    public const string HttpClientName = "google-oauth";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, DateTime> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public string RedirectUri => $"http://localhost:{jobPilot.Port}{GoogleOptions.CallbackPath}";

    public GoogleStatus Status()
    {
        var token = store.Read();
        return new GoogleStatus(options.CurrentValue.IsConfigured, token is not null, token?.Email, token?.Scopes ?? [], token?.ConnectedAtUtc);
    }

    /// <summary>The Google consent page URL; the state is single-use and expires in 10 minutes.</summary>
    public string BuildAuthorizationUrl(GoogleAccess access = GoogleAccess.Full)
    {
        var google = options.CurrentValue;
        if (!google.IsConfigured)
        {
            throw new InvalidOperationException("Falta configurar Google:ClientId y Google:ClientSecret (ver README).");
        }

        var now = time.GetUtcNow().UtcDateTime;
        foreach (var expired in _states.Where(s => s.Value < now).Select(s => s.Key))
        {
            _states.TryRemove(expired, out _);
        }

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _states[state] = now + StateLifetime;
        var query = new Dictionary<string, string>
        {
            ["client_id"] = google.ClientId!,
            ["redirect_uri"] = RedirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', GoogleScopes.For(access)),
            ["access_type"] = "offline",
            // Always show the consent screen so Google returns a refresh token on reconnection.
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true",
            ["state"] = state,
        };
        return google.AuthorizationEndpoint + "?" + string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));
    }

    /// <summary>Handles the redirect from Google: validates the state and exchanges the code for tokens.</summary>
    public async Task<GoogleResult<GoogleStatus>> CompleteAsync(string? code, string? state, string? error, CancellationToken cancellationToken)
    {
        if (state is null || !_states.TryRemove(state, out var expires) || expires < time.GetUtcNow().UtcDateTime)
        {
            return GoogleResult<GoogleStatus>.Fail("La respuesta de Google no corresponde a un pedido de esta app (o venció). Volvé a conectar.");
        }

        if (error is not null || string.IsNullOrEmpty(code))
        {
            return GoogleResult<GoogleStatus>.Fail(error == "access_denied" ? "Cancelaste el permiso en Google." : $"Google devolvió un error: {error}.");
        }

        var google = options.CurrentValue;
        var response = await PostTokenAsync(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = google.ClientId!,
            ["client_secret"] = google.ClientSecret!,
            ["redirect_uri"] = RedirectUri,
            ["grant_type"] = "authorization_code",
        }, cancellationToken);
        if (response.Value is not { } token)
        {
            return GoogleResult<GoogleStatus>.Fail(response.Error!);
        }

        var scopes = (token.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        store.Write(new GoogleToken(token.AccessToken, Expiry(token), token.RefreshToken, scopes, null, time.GetUtcNow().UtcDateTime));
        return GoogleResult<GoogleStatus>.Ok(Status());
    }

    /// <summary>Records the account e-mail (shown in the settings page) once it is known.</summary>
    public void SetEmail(string email)
    {
        if (store.Read() is { } token)
        {
            store.Write(token with { Email = email });
        }
    }

    /// <summary>A valid access token, refreshed when it is about to expire; null with an error when the owner must reconnect.</summary>
    public async Task<GoogleResult<string>> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (store.Read() is not { } token)
        {
            return GoogleResult<string>.Fail("Google no está conectado (Configuración → Google).");
        }

        if (token.ExpiresAtUtc > time.GetUtcNow().UtcDateTime.AddMinutes(1))
        {
            return GoogleResult<string>.Ok(token.AccessToken);
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            token = store.Read() ?? token;
            if (token.ExpiresAtUtc > time.GetUtcNow().UtcDateTime.AddMinutes(1))
            {
                return GoogleResult<string>.Ok(token.AccessToken);
            }

            if (token.RefreshToken is null)
            {
                return GoogleResult<string>.Fail("El permiso de Google venció. Volvé a conectar (Configuración → Google).");
            }

            var google = options.CurrentValue;
            var response = await PostTokenAsync(new Dictionary<string, string>
            {
                ["client_id"] = google.ClientId ?? string.Empty,
                ["client_secret"] = google.ClientSecret ?? string.Empty,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token",
            }, cancellationToken);
            if (response.Value is not { } refreshed)
            {
                if (response.Error == InvalidGrant)
                {
                    // Revoked, expired (7 days for "Testing" apps) or password changed: the stored token is useless.
                    store.Delete();
                    return GoogleResult<string>.Fail("El permiso de Google venció o fue revocado. Volvé a conectar (Configuración → Google).");
                }

                return GoogleResult<string>.Fail(response.Error!);
            }

            var scopes = refreshed.Scope is null ? token.Scopes : refreshed.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            store.Write(token with { AccessToken = refreshed.AccessToken, ExpiresAtUtc = Expiry(refreshed), RefreshToken = refreshed.RefreshToken ?? token.RefreshToken, Scopes = scopes });
            return GoogleResult<string>.Ok(refreshed.AccessToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Revokes the grant at Google (best effort) and deletes the local token.</summary>
    public async Task<string?> DisconnectAsync(CancellationToken cancellationToken)
    {
        var token = store.Read();
        store.Delete();
        var value = token?.RefreshToken ?? token?.AccessToken;
        if (value is null)
        {
            return null;
        }

        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsync(options.CurrentValue.RevocationEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = value }), cancellationToken);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest
                ? null
                : "Se borró el permiso local, pero Google no confirmó la revocación: revisalo en myaccount.google.com/permissions.";
        }
        catch (HttpRequestException)
        {
            return "Se borró el permiso local, pero no se pudo avisar a Google: revocalo en myaccount.google.com/permissions.";
        }
    }

    private const string InvalidGrant = "invalid_grant";

    private DateTime Expiry(TokenResponse token) => time.GetUtcNow().UtcDateTime.AddSeconds(Math.Max(60, token.ExpiresIn));

    private async Task<GoogleResult<TokenResponse>> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsync(options.CurrentValue.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
                return token?.AccessToken is null ? GoogleResult<TokenResponse>.Fail("Google devolvió un token vacío.") : GoogleResult<TokenResponse>.Ok(token);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            string? error = null;
            try
            {
                error = JsonDocument.Parse(body).RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            }
            catch (JsonException)
            {
            }

            return GoogleResult<TokenResponse>.Fail(error == InvalidGrant ? InvalidGrant : $"Google rechazó el pedido de token ({(int)response.StatusCode} {error}).");
        }
        catch (HttpRequestException)
        {
            return GoogleResult<TokenResponse>.Fail("No se pudo conectar con Google.");
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("scope")] string? Scope);
}
