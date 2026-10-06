using JobPilot.Mobile.Core.Infrastructure.Secrets;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Stand-in for Android's secure storage.</summary>
public sealed class InMemorySecretStore : ISecretStore
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> GetAsync(string key) => Task.FromResult(Values.GetValueOrDefault(key));

    public Task SetAsync(string key, string value)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }

    public void Remove(string key) => Values.Remove(key);
}
