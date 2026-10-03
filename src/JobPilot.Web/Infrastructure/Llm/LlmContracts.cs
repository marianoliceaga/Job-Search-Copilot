using JobPilot.Domain.Llm;
using JobPilot.Domain.Prompts;

namespace JobPilot.Web.Infrastructure.Llm;

/// <summary>Provider-agnostic LLM client. Use cases depend on this interface; tests use a fake.</summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends the prompt and returns a response deserialized into <typeparamref name="T"/>, whose JSON schema is
    /// sent to the model. Never throws for provider errors: they come back as <see cref="LlmResult{T}.Error"/>.
    /// Throws <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    Task<LlmResult<T>> GenerateAsync<T>(LlmRequest<T> request, CancellationToken cancellationToken)
        where T : class;

    /// <summary>
    /// Continues a conversation: the prompt's user message is the first turn and <paramref name="history"/> holds the
    /// following turns, alternating model and user and ending with a user turn. Same validation, retry and logging as
    /// <see cref="GenerateAsync{T}"/>.
    /// </summary>
    Task<LlmResult<T>> ChatAsync<T>(LlmRequest<T> request, IReadOnlyList<LlmTurn> history, CancellationToken cancellationToken)
        where T : class;
}

public enum LlmRole
{
    User,
    Model,
}

/// <summary>One turn of a conversation after the first user message.</summary>
public sealed record LlmTurn(LlmRole Role, string Text)
{
    /// <summary>Throws when the turns do not alternate model → user or do not end with a user turn.</summary>
    public static void EnsureValidHistory(IReadOnlyList<LlmTurn> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        for (var i = 0; i < history.Count; i++)
        {
            var expected = i % 2 == 0 ? LlmRole.Model : LlmRole.User;
            if (history[i].Role != expected)
            {
                throw new ArgumentException($"Turn {i + 1} should be {expected}: turns must alternate model and user.", nameof(history));
            }
        }

        if (history.Count % 2 != 0)
        {
            throw new ArgumentException("The conversation must end with a user turn.", nameof(history));
        }
    }
}

/// <summary>Lists the models available to the configured API key.</summary>
public interface ILlmModelCatalog
{
    Task<LlmResult<IReadOnlyList<LlmModelInfo>>> ListModelsAsync(CancellationToken cancellationToken);
}

public sealed record LlmModelInfo(string Name, string? DisplayName);

/// <param name="Prompt">Exactly what will be sent (shown to the owner before sending).</param>
public sealed record LlmRequest<T>(RenderedPrompt Prompt)
    where T : class
{
    /// <summary>Domain validation of the deserialized value. Returns the list of problems (empty when valid).</summary>
    public Func<T, IReadOnlyList<string>>? Validate { get; init; }

    /// <summary>
    /// Soft review: problems trigger the bounded retry like <see cref="Validate"/>, but on the last attempt the value is
    /// returned anyway with the problems in <see cref="LlmResult{T}.Warnings"/> so the UI can flag them.
    /// </summary>
    public Func<T, IReadOnlyList<string>>? Review { get; init; }

    /// <summary>
    /// Enables Gemini's URL context tool: the model may read the public URLs written in the prompt. The URLs it read are
    /// returned in <see cref="LlmResult{T}.RetrievedUrls"/>.
    /// </summary>
    public bool UseUrlContext { get; init; }
}

/// <param name="Status">Gemini's retrieval status, e.g. <c>URL_RETRIEVAL_STATUS_SUCCESS</c>.</param>
public sealed record LlmRetrievedUrl(string Url, string Status)
{
    public const string SuccessStatus = "URL_RETRIEVAL_STATUS_SUCCESS";

    public bool Succeeded => Status == SuccessStatus;
}

public sealed record LlmError(LlmErrorKind Kind, string Message, string? Detail = null);

public sealed record LlmResult<T>
{
    public T? Value { get; init; }

    public LlmError? Error { get; init; }

    public bool IsSuccess => Error is null;

    /// <summary>Problems found by <see cref="LlmRequest{T}.Review"/> on the value that was finally returned.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>URLs the URL context tool tried to read in any attempt of this request (empty when the tool was off).</summary>
    public IReadOnlyList<LlmRetrievedUrl> RetrievedUrls { get; init; } = [];

    public string Model { get; init; } = string.Empty;

    /// <summary>Usage summed across attempts.</summary>
    public LlmUsage Usage { get; init; } = LlmUsage.None;

    public decimal? EstimatedCostUsd { get; init; }

    public long LatencyMs { get; init; }

    public int Attempts { get; init; }

    public static LlmResult<T> Success(T value) => new() { Value = value };

    public static LlmResult<T> Failure(LlmErrorKind kind, string message, string? detail = null) =>
        new() { Error = new LlmError(kind, message, detail) };
}

/// <summary>Effective model parameters for a use case after applying owner overrides and configuration.</summary>
public sealed record ResolvedLlmSettings(
    string UseCase,
    string Model,
    string? ThinkingLevel,
    double? Temperature,
    int? MaxOutputTokens,
    bool IsOverridden);
