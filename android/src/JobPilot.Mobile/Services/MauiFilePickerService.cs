using JobPilot.Mobile.Core.Infrastructure.Files;

namespace JobPilot.Mobile.Services;

/// <summary>
/// <see cref="IFilePickerService"/> over MAUI's <see cref="FilePicker"/> (Android's document picker). Android filters by
/// MIME type; the file is read with <c>OpenReadAsync</c> (its path may be a <c>content://</c> URI) and never copied.
/// </summary>
public sealed class MauiFilePickerService : IFilePickerService
{
    private static readonly Dictionary<string, string[]> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
        // Many file managers do not know Markdown and report it as a generic binary; the extension is checked afterwards.
        [".md"] = ["text/markdown", "text/x-markdown", "application/octet-stream"],
        [".markdown"] = ["text/markdown", "text/x-markdown", "application/octet-stream"],
        [".txt"] = ["text/plain"],
    };

    public async Task<PickResult> PickDocumentAsync(string title, IReadOnlyList<string> extensions, long maxBytes, CancellationToken cancellationToken)
    {
        var types = extensions.SelectMany(e => MimeTypes.GetValueOrDefault(e, [])).Distinct().ToArray();
        var options = new PickOptions
        {
            PickerTitle = title,
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.Android] = types }),
        };

        try
        {
            // Permission checks and the picker must run on the UI thread.
            var result = await MainThread.InvokeOnMainThreadAsync(() => FilePicker.Default.PickAsync(options));
            return result is null ? PickResult.Cancelled : await ReadAsync(result, maxBytes, cancellationToken);
        }
        catch (PermissionException)
        {
            return new PickResult(null, "Android no dio permiso para leer archivos. Habilitalo en los ajustes de la app y volvé a intentar.");
        }
        catch (IOException ex)
        {
            return new PickResult(null, $"No se pudo leer el archivo: {ex.Message}");
        }
    }

    private static async Task<PickResult> ReadAsync(FileResult result, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await result.OpenReadAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return new PickResult(null, $"El archivo supera los {maxBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return new PickResult(new PickedFile(result.FileName, buffer.ToArray()));
    }
}
