using JobPilot.Mobile.Core.Infrastructure.Google;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Stand-in for Android's authorization API: answers with scripted results and records every request.</summary>
public sealed class FakeGoogleAuthorizer : IGoogleAuthorizer
{
    private readonly Queue<GoogleAuthorization> _results = new();

    public bool IsAvailable { get; set; } = true;

    /// <summary>Answer when nothing is queued: a fresh token for every scope asked.</summary>
    public Func<IReadOnlyList<string>, GoogleAuthorization> Default { get; set; } = scopes => new($"access-{Guid.NewGuid():N}", scopes, false, null);

    public List<(IReadOnlyList<string> Scopes, bool Interactive)> Requests { get; } = [];

    public List<string> Cleared { get; } = [];

    public FakeGoogleAuthorizer Returns(GoogleAuthorization result)
    {
        _results.Enqueue(result);
        return this;
    }

    public Task<GoogleAuthorization> AuthorizeAsync(IReadOnlyList<string> scopes, bool interactive, CancellationToken cancellationToken)
    {
        Requests.Add((scopes, interactive));
        return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : Default(scopes));
    }

    public Task ClearTokenAsync(string accessToken)
    {
        Cleared.Add(accessToken);
        return Task.CompletedTask;
    }
}
