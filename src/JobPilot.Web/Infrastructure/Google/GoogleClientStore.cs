using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace JobPilot.Web.Infrastructure.Google;

/// <summary>The owner's OAuth client ("Web application") as entered in the setup wizard.</summary>
public sealed record GoogleClientCredentials(string ClientId, string ClientSecret, DateTime SavedAtUtc);

/// <summary>Where the OAuth client in use comes from: configuration (user-secrets or environment) wins over the wizard.</summary>
public enum GoogleClientSource
{
    None,
    Configuration,
    App,
}

/// <summary>
/// Keeps the OAuth client entered in the setup wizard in <c>google-client.bin</c> in the data folder, encrypted with ASP.NET
/// Core Data Protection like the token (never in the repository, the database or an export file).
/// </summary>
public sealed class GoogleClientStore(DataPaths paths, IDataProtectionProvider protection, TimeProvider time)
{
    private const string ClientIdSuffix = ".apps.googleusercontent.com";

    private readonly IDataProtector _protector = protection.CreateProtector("JobPilot.GoogleClient.v1");

    public string FilePath => Path.Combine(paths.DataDirectory, "google-client.bin");

    public GoogleClientCredentials? Read()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GoogleClientCredentials>(_protector.Unprotect(File.ReadAllText(FilePath)), JsonSerializerOptions.Web);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Keys changed or the file was tampered with: the owner enters the client again.
            return null;
        }
    }

    /// <summary>Validates and stores the client. Returns an error message for the UI, or null when it was saved.</summary>
    public string? Save(string? clientId, string? clientSecret)
    {
        var id = clientId?.Trim() ?? string.Empty;
        var secret = clientSecret?.Trim() ?? string.Empty;
        if (id.Length == 0 || secret.Length == 0)
        {
            return "Pegá el Client ID y el Client secret.";
        }

        if (id.Any(char.IsWhiteSpace) || secret.Any(char.IsWhiteSpace))
        {
            return "El Client ID y el Client secret no llevan espacios. Revisá que los hayas copiado completos y sin texto extra.";
        }

        if (!id.EndsWith(ClientIdSuffix, StringComparison.Ordinal))
        {
            return $"El Client ID termina en \"{ClientIdSuffix}\". Revisá que no hayas pegado el secret en su lugar.";
        }

        Directory.CreateDirectory(paths.DataDirectory);
        var credentials = new GoogleClientCredentials(id, secret, time.GetUtcNow().UtcDateTime);
        File.WriteAllText(FilePath, _protector.Protect(JsonSerializer.Serialize(credentials, JsonSerializerOptions.Web)));
        return null;
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
