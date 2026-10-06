using JobPilot.Mobile.Core.Infrastructure.Files;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Stand-in for Android's document picker: returns the scripted result and records the request.</summary>
public sealed class FakeFilePicker : IFilePickerService
{
    public PickResult Next { get; set; } = PickResult.Cancelled;

    public List<(string Title, IReadOnlyList<string> Extensions, long MaxBytes)> Requests { get; } = [];

    public Task<PickResult> PickDocumentAsync(string title, IReadOnlyList<string> extensions, long maxBytes, CancellationToken cancellationToken)
    {
        Requests.Add((title, extensions, maxBytes));
        return Task.FromResult(Next);
    }
}
