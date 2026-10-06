using System.Text.RegularExpressions;

namespace JobPilot.Mobile.Tests.Persistence;

/// <summary>
/// The phone database keeps the web app's tables and mappings (names, columns, lengths, conversions, indexes), so the
/// export/import file of phase A10 maps one to one. Compared through both EF Core model snapshots.
/// </summary>
public partial class WebSchemaTests
{
    [Fact]
    public void Every_phone_table_is_mapped_like_the_web_table()
    {
        var web = Entities(Snapshot(Path.Combine(AndroidFolder.RepositoryRoot, "src", "JobPilot.Web", "Infrastructure", "Persistence", "Migrations")));
        var phone = Entities(Snapshot(Path.Combine(AndroidFolder.Root, "src", "JobPilot.Mobile.Core", "Infrastructure", "Persistence", "Migrations")));

        Assert.NotEmpty(phone);
        Assert.All(phone, entity =>
        {
            Assert.True(web.TryGetValue(entity.Key, out var webMapping), $"{entity.Key} does not exist in the web app.");
            Assert.True(webMapping == entity.Value, $"{entity.Key} is mapped differently from the web app.");
        });
    }

    private static string Snapshot(string directory) => File.ReadAllText(Directory.GetFiles(directory, "*ModelSnapshot.cs").Single());

    /// <summary>Entity name (without namespace) → normalized mapping code of its snapshot blocks.</summary>
    private static Dictionary<string, string> Entities(string snapshot)
    {
        var result = new Dictionary<string, List<string>>();
        foreach (Match match in EntityStart().Matches(snapshot))
        {
            var depth = 1;
            var index = match.Index + match.Length;
            var start = index;
            while (depth > 0)
            {
                depth += snapshot[index] switch { '{' => 1, '}' => -1, _ => 0 };
                index++;
            }

            var body = snapshot[start..(index - 1)];
            if (body.Contains("b.Property", StringComparison.Ordinal) || body.Contains("b.ToTable", StringComparison.Ordinal))
            {
                var name = match.Groups["name"].Value.Split('.')[^1];
                if (!result.TryGetValue(name, out var parts))
                {
                    result[name] = parts = [];
                }

                parts.Add(Whitespace().Replace(body, " "));
            }
        }

        return result.ToDictionary(e => e.Key, e => string.Join(" ", e.Value));
    }

    [GeneratedRegex("""modelBuilder\.Entity\("(?<name>[^"]+)", b =>\s*\{""")]
    private static partial Regex EntityStart();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
