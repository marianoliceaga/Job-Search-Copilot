namespace JobPilot.Domain.Llm;

/// <summary>Audit record of one request sent to the LLM provider (one per attempt).</summary>
public sealed class LlmCallRecord
{
    public long Id { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public required string UseCase { get; set; }

    public required string Model { get; set; }

    /// <summary>Identifier of the prompt template, e.g. <c>connection-test.v1</c>.</summary>
    public required string PromptId { get; set; }

    /// <summary>1 for the first attempt; higher when the output was invalid and the request was retried.</summary>
    public int Attempt { get; set; } = 1;

    public LlmCallOutcome Outcome { get; set; }

    public LlmErrorKind? ErrorKind { get; set; }

    public string? ErrorMessage { get; set; }

    public int PromptTokens { get; set; }

    public int OutputTokens { get; set; }

    public int ThoughtsTokens { get; set; }

    public int CachedTokens { get; set; }

    public int ToolUsePromptTokens { get; set; }

    public long LatencyMs { get; set; }

    /// <summary>Estimated cost; <see langword="null"/> when the model has no configured price.</summary>
    public decimal? EstimatedCostUsd { get; set; }

    public string? FinishReason { get; set; }

    /// <summary>Provider response identifier, useful when reporting problems.</summary>
    public string? ResponseId { get; set; }
}
