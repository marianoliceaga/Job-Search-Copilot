namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Disposable temporary folder (keeps test databases out of the owner's data directory).</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jobpilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // SQLite may still hold the file briefly on some platforms; the OS temp folder is cleaned eventually.
        }
    }
}
