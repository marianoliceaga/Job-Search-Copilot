namespace JobPilot.Mobile.Core.Infrastructure.Files;

/// <summary>A file the owner chose, read into memory (nothing is copied to the app's storage).</summary>
public sealed record PickedFile(string FileName, byte[] Content);

/// <param name="File">The file, or null when the owner cancelled.</param>
/// <param name="Error">A message for the UI when the file could not be read (e.g. too large).</param>
public sealed record PickResult(PickedFile? File, string? Error = null)
{
    public static PickResult Cancelled { get; } = new(File: null);
}

/// <summary>Android's document picker (implemented by the host); the owner chooses the file every time.</summary>
public interface IFilePickerService
{
    /// <param name="title">Shown by Android before the picker opens.</param>
    /// <param name="extensions">Accepted extensions (e.g. <c>.pdf</c>); the host maps them to Android MIME types.</param>
    /// <param name="maxBytes">Larger files are rejected without reading them whole.</param>
    Task<PickResult> PickDocumentAsync(string title, IReadOnlyList<string> extensions, long maxBytes, CancellationToken cancellationToken);
}
