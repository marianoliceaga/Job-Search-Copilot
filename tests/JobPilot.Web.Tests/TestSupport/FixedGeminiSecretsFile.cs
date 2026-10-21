using JobPilot.Web.Infrastructure.Llm;

namespace JobPilot.Web.Tests.TestSupport;

/// <summary>Stands in for the owner's secrets.json, so tests never read the real user-secrets file of the machine.</summary>
public sealed class FixedGeminiSecretsFile(string? key = null) : IGeminiSecretsFile
{
    public string? FilePath => "/fake/UserSecrets/jobpilot-web-local/secrets.json";

    public string? ReadKey() => string.IsNullOrWhiteSpace(key) ? null : key;
}
