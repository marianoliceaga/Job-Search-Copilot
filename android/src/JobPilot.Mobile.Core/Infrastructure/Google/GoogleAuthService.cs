using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace JobPilot.Mobile.Core.Infrastructure.Google;

/// <param name="Configured">Google Play services is available (the Android OAuth client itself can only be checked by connecting).</param>
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
/// Google authorization on the phone. Unlike the web app there are no stored tokens: the access token comes from
/// Android's authorization API each session and lives in memory; only the connection facts (granted scopes, e-mail,
/// date) are kept in <c>google-connection.json</c> in the app's private folder.
/// </summary>
public sealed class GoogleAuthService(
    IGoogleAuthorizer authorizer,
    IHttpClientFactory httpFactory,
    IOptionsMonitor<GoogleOptions> options,
    MobilePaths paths,
    TimeProvider time)
{
    public const string HttpClientName = "google-oauth";

    /// <summary>Android does not report the token's lifetime; Google documents one hour, so it is reused for less.</summary>
    internal static readonly TimeSpan TokenReuse = TimeSpan.FromMinutes(45);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _tokenUntilUtc;

    private string ConnectionPath => Path.Combine(paths.DataDirectory, "google-connection.json");

    public GoogleStatus Status()
    {
        var connection = Read();
        return new GoogleStatus(authorizer.IsAvailable, connection is not null, connection?.Email, connection?.Scopes ?? [], connection?.ConnectedAtUtc);
    }

    /// <summary>Shows Google's consent screen for the chosen scopes and records what was granted.</summary>
    public async Task<GoogleResult<GoogleStatus>> ConnectAsync(GoogleAccess access, CancellationToken cancellationToken)
    {
        if (!authorizer.IsAvailable)
        {
            return GoogleResult<GoogleStatus>.Fail("Este teléfono no tiene Google Play services, que Android necesita para conectar Google.");
        }

        var result = await authorizer.AuthorizeAsync(GoogleScopes.For(access), interactive: true, cancellationToken);
        if (result.AccessToken is null)
        {
            return GoogleResult<GoogleStatus>.Fail(result.Error ?? "Google no dio el permiso.");
        }

        var granted = GoogleScopes.For(access).Where(result.GrantedScopes.Contains).ToList();
        if (granted.Count == 0)
        {
            return GoogleResult<GoogleStatus>.Fail("No aceptaste ningún permiso en Google.");
        }

        Write(new GoogleConnection(granted, null, time.GetUtcNow().UtcDateTime));
        Remember(result.AccessToken);
        return GoogleResult<GoogleStatus>.Ok(Status());
    }

    /// <summary>Records the account e-mail (shown in the settings page) once it is known.</summary>
    public void SetEmail(string email)
    {
        if (Read() is { } connection)
        {
            Write(connection with { Email = email });
        }
    }

    /// <summary>A token for the granted scopes, asked silently to Android; an error when the owner must reconnect.</summary>
    public async Task<GoogleResult<string>> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (Read() is not { } connection)
        {
            return GoogleResult<string>.Fail("Google no está conectado (Configuración → Google).");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && _tokenUntilUtc > time.GetUtcNow().UtcDateTime)
            {
                return GoogleResult<string>.Ok(_token);
            }

            var result = await authorizer.AuthorizeAsync(connection.Scopes, interactive: false, cancellationToken);
            if (result.AccessToken is null)
            {
                return GoogleResult<string>.Fail(result.NeedsConsent
                    ? "Google pide que vuelvas a dar el permiso (puede haber vencido o haber sido revocado). Volvé a conectar en Configuración → Google."
                    : result.Error ?? "Google no dio el permiso.");
            }

            Remember(result.AccessToken);
            return GoogleResult<string>.Ok(result.AccessToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Google rejected the token (401): drop it here and in Android's cache so the next call gets a new one.</summary>
    public async Task ForgetTokenAsync(string accessToken)
    {
        await _lock.WaitAsync();
        try
        {
            if (_token == accessToken)
            {
                _token = null;
            }
        }
        finally
        {
            _lock.Release();
        }

        await authorizer.ClearTokenAsync(accessToken);
    }

    /// <summary>Revokes the grant at Google (best effort) and forgets the connection.</summary>
    public async Task<string?> DisconnectAsync(CancellationToken cancellationToken)
    {
        string? token = null;
        if (Read() is not null)
        {
            token = (await GetAccessTokenAsync(cancellationToken)).Value;
        }

        File.Delete(ConnectionPath);
        _token = null;
        if (token is null)
        {
            return "Se borró la conexión en el teléfono, pero no se pudo avisar a Google: revocá el permiso en myaccount.google.com/permissions.";
        }

        await authorizer.ClearTokenAsync(token);
        try
        {
            using var http = httpFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsync(options.CurrentValue.RevocationEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }), cancellationToken);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest
                ? null
                : "Se borró la conexión en el teléfono, pero Google no confirmó la revocación: revisala en myaccount.google.com/permissions.";
        }
        catch (HttpRequestException)
        {
            return "Se borró la conexión en el teléfono, pero no se pudo avisar a Google: revocá el permiso en myaccount.google.com/permissions.";
        }
    }

    private void Remember(string token)
    {
        _token = token;
        _tokenUntilUtc = time.GetUtcNow().UtcDateTime + TokenReuse;
    }

    private GoogleConnection? Read()
    {
        try
        {
            return File.Exists(ConnectionPath) ? JsonSerializer.Deserialize<GoogleConnection>(File.ReadAllText(ConnectionPath)) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Write(GoogleConnection connection)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(ConnectionPath, JsonSerializer.Serialize(connection));
    }

    /// <summary>What is kept about the connection: no tokens.</summary>
    private sealed record GoogleConnection(IReadOnlyList<string> Scopes, string? Email, DateTime ConnectedAtUtc);
}
