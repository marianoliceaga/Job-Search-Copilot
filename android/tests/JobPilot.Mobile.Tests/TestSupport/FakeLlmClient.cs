using JobPilot.Domain.Prompts;
using JobPilot.Mobile.Core.Infrastructure.Llm;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>In-memory <see cref="ILlmClient"/> for use case tests: returns scripted values and records prompts.</summary>
public sealed class FakeLlmClient : ILlmClient
{
    private readonly Queue<object> _responses = new();

    public List<RenderedPrompt> Prompts { get; } = [];

    /// <summary>Problems reported by the use case's validator for the scripted values.</summary>
    public List<string> ValidationProblems { get; } = [];

    /// <summary>Whether each request enabled the URL context tool.</summary>
    public List<bool> UrlContextFlags { get; } = [];

    /// <summary>URLs reported as read by the URL context tool on successful results.</summary>
    public List<LlmRetrievedUrl> RetrievedUrls { get; } = [];

    public FakeLlmClient Returns(object value)
    {
        _responses.Enqueue(value);
        return this;
    }

    public FakeLlmClient Fails(LlmError error)
    {
        _responses.Enqueue(error);
        return this;
    }

    /// <summary>History received by each call (empty for single-turn requests).</summary>
    public List<IReadOnlyList<LlmTurn>> Histories { get; } = [];

    public Task<LlmResult<T>> GenerateAsync<T>(LlmRequest<T> request, CancellationToken cancellationToken)
        where T : class =>
        ChatAsync(request, [], cancellationToken);

    public Task<LlmResult<T>> ChatAsync<T>(LlmRequest<T> request, IReadOnlyList<LlmTurn> history, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(request);
        LlmTurn.EnsureValidHistory(history);
        Histories.Add(history.ToList());
        cancellationToken.ThrowIfCancellationRequested();
        Prompts.Add(request.Prompt);
        UrlContextFlags.Add(request.UseUrlContext);

        var next = _responses.Dequeue();
        if (next is LlmError error)
        {
            return Task.FromResult(new LlmResult<T> { Error = error, Model = "fake", Attempts = 1 });
        }

        var value = (T)next;
        var problems = request.Validate?.Invoke(value) ?? [];
        ValidationProblems.AddRange(problems);
        var review = request.Review?.Invoke(value) ?? [];
        return Task.FromResult(problems.Count == 0
            ? LlmResult<T>.Success(value) with { Model = "fake", Attempts = 1, Warnings = review, RetrievedUrls = request.UseUrlContext ? RetrievedUrls.ToList() : [] }
            : new LlmResult<T> { Error = new LlmError(Domain.Llm.LlmErrorKind.InvalidOutput, "invalid", string.Join("; ", problems)), Model = "fake", Attempts = 1 });
    }
}
