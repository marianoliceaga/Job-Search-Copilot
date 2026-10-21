using Microsoft.AspNetCore.DataProtection;

namespace JobPilot.Web.Infrastructure.Llm;

/// <summary>Where the Gemini API key in use comes from: the key saved in the app wins over <c>secrets.json</c>.</summary>
public enum GeminiKeySource
{
    None,
    SecretsFile,
    App,
}

/// <summary>
/// The Gemini API key. Two places only, in this order: the key pasted in Configuración, kept in <c>gemini-key.bin</c> in the
/// data folder and encrypted with ASP.NET Core Data Protection (never in the repository, the database or an export file); then
/// <c>Gemini:ApiKey</c> in the user-secrets file (<see cref="IGeminiSecretsFile"/>). Environment variables are never read for
/// the key. It is never shown back in the UI.
/// </summary>
public sealed class GeminiKeyStore(DataPaths paths, IDataProtectionProvider protection, IGeminiSecretsFile secrets)
{
    private readonly IDataProtector _protector = protection.CreateProtector("JobPilot.GeminiKey.v1");
    private readonly Lock _gate = new();
    private string? _cached;
    private bool _loaded;

    public string FilePath => Path.Combine(paths.DataDirectory, "gemini-key.bin");

    /// <summary>The user-secrets file, for the UI (null when its location cannot be resolved).</summary>
    public string? SecretsFilePath => secrets.FilePath;

    public bool HasAppKey => StoredKey() is not null;

    public bool HasSecretsFileKey => secrets.ReadKey() is not null;

    public GeminiKeySource Source => HasAppKey ? GeminiKeySource.App
        : HasSecretsFileKey ? GeminiKeySource.SecretsFile
        : GeminiKeySource.None;

    /// <summary>The key to send: the one saved in the app first, then the one in <c>secrets.json</c>.</summary>
    public string? ApiKey => StoredKey() ?? secrets.ReadKey();

    /// <summary>Validates and stores the key (trimmed). Returns an error message for the UI, or null when it was saved.</summary>
    public string? Save(string? value)
    {
        var key = value?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return "Pegá la API key antes de guardarla.";
        }

        if (key.Any(char.IsWhiteSpace))
        {
            return "La API key no puede tener espacios. Revisá que la hayas copiado completa y sin texto extra.";
        }

        Directory.CreateDirectory(paths.DataDirectory);
        lock (_gate)
        {
            File.WriteAllText(FilePath, _protector.Protect(key));
            (_cached, _loaded) = (key, true);
        }

        return null;
    }

    public void Delete()
    {
        lock (_gate)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            (_cached, _loaded) = (null, true);
        }
    }

    private string? StoredKey()
    {
        lock (_gate)
        {
            if (!_loaded)
            {
                _cached = Load();
                _loaded = true;
            }

            return _cached;
        }
    }

    private string? Load()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var key = _protector.Unprotect(File.ReadAllText(FilePath));
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Keys changed or the file was tampered with: the owner pastes the key again.
            return null;
        }
    }
}
