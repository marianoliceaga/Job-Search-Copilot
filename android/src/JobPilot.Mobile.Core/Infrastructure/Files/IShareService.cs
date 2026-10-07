namespace JobPilot.Mobile.Core.Infrastructure.Files;

/// <summary>
/// Android's share sheet (implemented by the host): the owner chooses where the file goes (Drive, Files, an e-mail they
/// write themselves...). The app never sends anything on its own.
/// </summary>
public interface IShareService
{
    Task ShareFileAsync(string title, string fileName, string contentType, byte[] content);
}
