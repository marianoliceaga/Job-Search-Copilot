namespace JobPilot.Mobile.Core.Infrastructure.Files;

/// <summary>
/// Android's share sheet and clipboard (implemented by the host): the owner chooses where a file or text goes (Drive,
/// Files, an e-mail or message they send themselves...). The app never sends anything on its own.
/// </summary>
public interface IShareService
{
    Task ShareFileAsync(string title, string fileName, string contentType, byte[] content);

    /// <param name="subject">Used by e-mail apps as the subject; the owner reviews and sends the message there.</param>
    Task ShareTextAsync(string title, string? subject, string text);

    Task CopyTextAsync(string text);
}
