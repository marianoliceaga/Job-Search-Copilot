using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace JobPilot.Web.Infrastructure.Google;

/// <param name="Scopes">Scopes the owner actually granted (granular consent may leave some out).</param>
public sealed record GoogleToken(
    string AccessToken,
    DateTime ExpiresAtUtc,
    string? RefreshToken,
    IReadOnlyList<string> Scopes,
    string? Email,
    DateTime ConnectedAtUtc);

/// <summary>
/// Keeps the OAuth token in a file in the local data folder, encrypted with ASP.NET Core Data Protection (keys also in
/// the data folder; protected with DPAPI on Windows). Never in the repository or the database.
/// </summary>
public sealed class GoogleTokenStore(DataPaths paths, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("JobPilot.GoogleToken.v1");

    public string FilePath => Path.Combine(paths.DataDirectory, "google-token.bin");

    public GoogleToken? Read()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<GoogleToken>(_protector.Unprotect(File.ReadAllText(FilePath)), JsonSerializerOptions.Web);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Keys changed or the file was tampered with: the owner has to connect again.
            return null;
        }
    }

    public void Write(GoogleToken token)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        File.WriteAllText(FilePath, _protector.Protect(JsonSerializer.Serialize(token, JsonSerializerOptions.Web)));
    }

    public void Delete()
    {
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
    }
}
