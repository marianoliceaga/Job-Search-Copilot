using JobPilot.Mobile.Core.Infrastructure.Secrets;
using JobPilot.Mobile.Tests.TestSupport;

namespace JobPilot.Mobile.Tests.Features;

public class GeminiKeyStoreTests
{
    [Fact]
    public async Task The_key_is_trimmed_stored_in_the_secret_store_and_removed()
    {
        var secrets = new InMemorySecretStore();
        var keys = new GeminiKeyStore(secrets);
        var changes = 0;
        keys.Changed += () => changes++;

        Assert.False(await keys.HasKeyAsync());
        Assert.Null(await keys.SaveAsync("  AIza-test-key \n"));
        Assert.Equal("AIza-test-key", secrets.Values[GeminiKeyStore.Key]);
        Assert.True(await keys.HasKeyAsync());

        keys.Remove();
        Assert.Empty(secrets.Values);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData(null, "Pegá la API key")]
    [InlineData("   ", "Pegá la API key")]
    [InlineData("two words", "no puede tener espacios")]
    public async Task Invalid_keys_are_not_saved(string? value, string message)
    {
        var secrets = new InMemorySecretStore();

        Assert.Contains(message, await new GeminiKeyStore(secrets).SaveAsync(value), StringComparison.Ordinal);
        Assert.Empty(secrets.Values);
    }
}
