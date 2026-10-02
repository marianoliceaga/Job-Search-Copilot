namespace JobPilot.Domain.Llm;

/// <summary>Token usage reported by the LLM provider for a single call.</summary>
/// <param name="PromptTokens">Input tokens, including <paramref name="CachedTokens"/>.</param>
/// <param name="OutputTokens">Visible output tokens.</param>
/// <param name="ThoughtsTokens">Reasoning tokens (billed as output).</param>
/// <param name="CachedTokens">Part of the prompt served from the context cache.</param>
/// <param name="ToolUsePromptTokens">Input tokens added by built-in tools (e.g. search grounding).</param>
public sealed record LlmUsage(
    int PromptTokens,
    int OutputTokens,
    int ThoughtsTokens = 0,
    int CachedTokens = 0,
    int ToolUsePromptTokens = 0)
{
    public static LlmUsage None { get; } = new(0, 0);

    public int InputTokens => PromptTokens + ToolUsePromptTokens;

    public int BilledOutputTokens => OutputTokens + ThoughtsTokens;
}
