using JobPilot.Mobile.Core.Infrastructure.Secrets;
using Microsoft.Extensions.Logging;

namespace JobPilot.Mobile.Services;

/// <summary>
/// <see cref="ISecretStore"/> over MAUI's <see cref="SecureStorage"/> (on Android, values encrypted with a key kept in the
/// Android Keystore). Excluded from cloud backups by <c>allowBackup=false</c>.
/// </summary>
public sealed partial class MauiSecretStore(ILogger<MauiSecretStore> logger) : ISecretStore
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The value can no longer be decrypted (e.g. the keystore entry was reset): it is treated as missing and the
            // owner enters it again; the value itself is never logged.
            LogReadFailed(ex, key);
            return null;
        }
    }

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Default.Remove(key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the secret {Key} from secure storage.")]
    private partial void LogReadFailed(Exception exception, string key);
}
