using Microsoft.Extensions.Configuration;

namespace JobPilot.Mobile.Core.Infrastructure;

/// <summary>
/// Configuration of the Android app: the web app's <c>appsettings.json</c>, embedded at build time (models, parameters and
/// prices per use case), so both apps share the same defaults. Secrets are never part of it.
/// </summary>
public static class MobileConfiguration
{
    public const string WebDefaultsResource = "web-appsettings.json";

    public static IConfiguration Load(IEnumerable<KeyValuePair<string, string?>>? overrides = null)
    {
        using var stream = typeof(MobileConfiguration).Assembly.GetManifestResourceStream(WebDefaultsResource)
            ?? throw new InvalidOperationException($"Embedded resource '{WebDefaultsResource}' not found.");
        // AddJsonStream reads the stream during Build, before it is disposed.
        return new ConfigurationBuilder()
            .AddJsonStream(stream)
            .AddInMemoryCollection(overrides ?? [])
            .Build();
    }
}
