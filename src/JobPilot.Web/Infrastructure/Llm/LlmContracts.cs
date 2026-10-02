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
}

public sealed record LlmError(LlmErrorKind Kind, string Message, string? Detail = null);

public sealed record LlmResult<T>
{
    public T? Value { get; init; }

    public LlmError? Error { get; init; }

    public bool IsSuccess => Error is null;

    /// <summary>Problems found by <see cref="LlmRequest{T}.Review"/> on the value that was finally returned.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

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
