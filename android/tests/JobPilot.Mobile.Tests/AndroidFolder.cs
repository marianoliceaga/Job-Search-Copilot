namespace JobPilot.Mobile.Tests;

/// <summary>Locates the android/ folder (the one with JobPilot.Mobile.slnx) from the test output directory.</summary>
internal static class AndroidFolder
{
    public static string Root { get; } = Find();

    /// <summary>Source files of the Android app (android/src), without build output.</summary>
    public static IReadOnlyList<string> SourceFiles(params string[] extensions) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JobPilot.Mobile.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("android/ folder not found.");
    }
}
