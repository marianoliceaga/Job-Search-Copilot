namespace JobPilot.Mobile.Core.Infrastructure.Secrets;

/// <summary>The owner's Gemini API key, kept in the secret store. It is never shown back in the UI.</summary>
public sealed class GeminiKeyStore(ISecretStore secrets)
{
    public const string Key = "gemini-api-key";

    public event Action? Changed;

    public async Task<string?> GetAsync()
    {
        var value = await secrets.GetAsync(Key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task<bool> HasKeyAsync() => await GetAsync() is not null;

    /// <summary>Stores the key (trimmed). Returns an error message for the UI, or null when it was saved.</summary>
    public async Task<string?> SaveAsync(string? value)
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

        await secrets.SetAsync(Key, key);
        Changed?.Invoke();
        return null;
    }

    public void Remove()
    {
        secrets.Remove(Key);
        Changed?.Invoke();
    }
}
