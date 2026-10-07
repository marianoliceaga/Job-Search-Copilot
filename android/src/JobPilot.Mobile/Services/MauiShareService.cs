using JobPilot.Mobile.Core.Infrastructure.Files;

namespace JobPilot.Mobile.Services;

/// <summary>
/// <see cref="IShareService"/> over MAUI's <see cref="Share"/>: the file is written to <c>cache/sharing-root</c> (the only
/// folder the FileProvider exposes) and handed to Android's share sheet. Earlier exports are deleted first and on every
/// app start: exported files are not kept.
/// </summary>
public sealed class MauiShareService : IShareService
{
    public static string SharingRoot => Path.Combine(FileSystem.CacheDirectory, "sharing-root");

    public async Task ShareFileAsync(string title, string fileName, string contentType, byte[] content)
    {
        ClearSharedFiles();
        Directory.CreateDirectory(SharingRoot);
        var path = Path.Combine(SharingRoot, Path.GetFileName(fileName));
        await File.WriteAllBytesAsync(path, content);

        await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(path, contentType),
        }));
    }

    /// <summary>Deletes files left from earlier shares (the receiving app has already copied them).</summary>
    public static void ClearSharedFiles()
    {
        if (Directory.Exists(SharingRoot))
        {
            Directory.Delete(SharingRoot, recursive: true);
        }
    }
}
