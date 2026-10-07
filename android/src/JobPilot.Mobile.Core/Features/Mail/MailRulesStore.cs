using System.Text.Json;
using JobPilot.Domain.Mail;
using JobPilot.Mobile.Core.Infrastructure.Persistence;

namespace JobPilot.Mobile.Core.Features.Mail;

/// <summary>
/// The owner's mail rules: a settings document, seeded from <c>Seed/mail-rules.seed.json</c> (generic, no personal data) and
/// edited as JSON. Saving compiles every rule first, so a typo is reported instead of breaking the next check.
/// </summary>
public sealed class MailRulesStore(SettingsDocumentStore documents, MailRulesSeed seed)
{
    private const string Key = "mail-rules";

    private static readonly JsonSerializerOptions Indented = new(DomainJson.Options) { WriteIndented = true };

    public async Task<MailRules> GetAsync(CancellationToken cancellationToken) =>
        await documents.GetAsync<MailRules>(Key, cancellationToken) ?? seed.Load();

    public async Task<string> GetJsonAsync(CancellationToken cancellationToken) =>
        JsonSerializer.Serialize(await GetAsync(cancellationToken), Indented);

    public static string DefaultJson(MailRulesSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        return JsonSerializer.Serialize(seed.Load(), Indented);
    }

    /// <summary>Validates and saves the rules. Returns a Spanish error, or null when saved.</summary>
    public async Task<string?> SaveJsonAsync(string json, CancellationToken cancellationToken)
    {
        var (rules, error) = Parse(json);
        if (rules is null)
        {
            return error;
        }

        await documents.SetAsync(Key, rules, cancellationToken);
        return null;
    }

    public static (MailRules? Rules, string? Error) Parse(string json)
    {
        MailRules? rules;
        try
        {
            rules = JsonSerializer.Deserialize<MailRules>(json, DomainJson.Options);
        }
        catch (JsonException ex)
        {
            return (null, $"El JSON no es válido: {ex.Message}");
        }

        if (rules is null)
        {
            return (null, "El JSON está vacío.");
        }

        try
        {
            MailClassifier.Compile(rules);
        }
        catch (MailRulesException ex)
        {
            return (null, ex.Message);
        }

        return rules.Gmail.MaxThreadsPerQuery is < 1 or > 500
            ? (null, "gmail.maxThreadsPerQuery tiene que estar entre 1 y 500.")
            : (rules, null);
    }
}

/// <summary>The generic rules shipped in the repository: the web app's <c>Seed/mail-rules.seed.json</c>, embedded at build time.</summary>
public sealed class MailRulesSeed
{
    private readonly Func<Stream?> _open;

    public MailRulesSeed()
        : this(() => typeof(MailRulesSeed).Assembly.GetManifestResourceStream("Seed/mail-rules.seed.json"))
    {
    }

    /// <param name="open">Opens the seed, or returns null when there is none.</param>
    internal MailRulesSeed(Func<Stream?> open) => _open = open;

    public MailRules Load()
    {
        using var stream = _open();
        return stream is null ? new MailRules() : JsonSerializer.Deserialize<MailRules>(stream, DomainJson.Options) ?? new MailRules();
    }
}
