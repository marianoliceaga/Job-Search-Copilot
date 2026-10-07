using JobPilot.Mobile.Core.Infrastructure.Files;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Stand-in for Android's share sheet and clipboard: records what would be shared or copied.</summary>
public sealed class FakeShareService : IShareService
{
    public List<(string Title, string FileName, string ContentType, byte[] Content)> Shared { get; } = [];

    public Task ShareFileAsync(string title, string fileName, string contentType, byte[] content)
    {
        Shared.Add((title, fileName, contentType, content));
        return Task.CompletedTask;
    }

    public List<(string Title, string? Subject, string Text)> SharedTexts { get; } = [];

    public List<string> Copied { get; } = [];

    public Task ShareTextAsync(string title, string? subject, string text)
    {
        SharedTexts.Add((title, subject, text));
        return Task.CompletedTask;
    }

    public Task CopyTextAsync(string text)
    {
        Copied.Add(text);
        return Task.CompletedTask;
    }
}
