using JobPilot.Domain.Prompts;

namespace JobPilot.Web.Infrastructure.Llm;

/// <summary>Versioned prompt files loaded at startup. The highest version of each use case is the active one.</summary>
public sealed class PromptLibrary
{
    private readonly Dictionary<string, PromptTemplate> _latest;

    public PromptLibrary(IEnumerable<PromptTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        var all = templates.ToList();

        var duplicate = all.GroupBy(t => t.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate prompt '{duplicate.Key}'.");
        }

        _latest = all.GroupBy(t => t.UseCase, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.MaxBy(t => t.Version)!, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<PromptTemplate> Active => _latest.Values;

    public static PromptLibrary LoadFrom(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Prompt folder not found: {directory}");
        }

        return new PromptLibrary(Directory.EnumerateFiles(directory, "*.md")
            .Select(path => PromptTemplate.Parse(path, File.ReadAllText(path))));
    }

    public PromptTemplate Get(string useCase) =>
        _latest.TryGetValue(useCase, out var template)
            ? template
            : throw new KeyNotFoundException($"No prompt file for use case '{useCase}'.");
}
