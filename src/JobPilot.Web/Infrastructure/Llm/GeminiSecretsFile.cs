using System.Reflection;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace JobPilot.Web.Infrastructure.Llm;

/// <summary>The Gemini key in the user-secrets file (<c>secrets.json</c>); replaced by a fixed value in tests.</summary>
public interface IGeminiSecretsFile
{
    string? FilePath { get; }

    string? ReadKey();
}

/// <summary>
/// Reads <c>Gemini:ApiKey</c> from this app's user-secrets file only
/// (<c>%APPDATA%\Microsoft\UserSecrets\jobpilot-web-local\secrets.json</c> on Windows), not from the merged configuration, so
/// environment variables and other configuration sources never supply the key. The file is watched: an edit applies without
/// restarting the app.
/// </summary>
public sealed class UserSecretsGeminiFile : IGeminiSecretsFile, IDisposable
{
    public const string Key = "Gemini:ApiKey";

    private readonly IConfigurationRoot? _configuration;

    public UserSecretsGeminiFile()
    {
        var assembly = typeof(UserSecretsGeminiFile).Assembly;
        var id = assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;
        if (id is null)
        {
            return;
        }

        try
        {
            FilePath = PathHelper.GetSecretsPathFromSecretsId(id);
            _configuration = new ConfigurationBuilder().AddUserSecrets(assembly, optional: true, reloadOnChange: true).Build();
        }
        catch (InvalidOperationException)
        {
            // No user profile folder (APPDATA / HOME): there is no secrets file to read.
            FilePath = null;
        }
    }

    public string? FilePath { get; }

    public string? ReadKey() => _configuration?[Key] is { } key && !string.IsNullOrWhiteSpace(key) ? key.Trim() : null;

    public void Dispose() => (_configuration as IDisposable)?.Dispose();
}
