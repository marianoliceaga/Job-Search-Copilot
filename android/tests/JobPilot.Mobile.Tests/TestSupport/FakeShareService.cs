using JobPilot.Mobile.Core.Infrastructure.Files;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Stand-in for Android's share sheet: records what would be shared.</summary>
public sealed class FakeShareService : IShareService
{
    public List<(string Title, string FileName, string ContentType, byte[] Content)> Shared { get; } = [];

    public Task ShareFileAsync(string title, string fileName, string contentType, byte[] content)
    {
        Shared.Add((title, fileName, contentType, content));
        return Task.CompletedTask;
    }
}
