namespace JobPilot.Mobile.Core.Infrastructure.Secrets;

/// <summary>
/// Encrypted key-value storage for secrets (Android's secure storage in the app, a fake in tests).
/// Secrets never go to the database, preferences, logs or the repository.
/// </summary>
public interface ISecretStore
{
    /// <summary>The stored value, or null when there is none or it can no longer be read.</summary>
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}
