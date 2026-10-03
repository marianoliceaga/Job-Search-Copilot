namespace JobPilot.Domain.Prompts;

/// <summary>A prompt ready to be sent: exactly what the LLM provider will receive.</summary>
public sealed record RenderedPrompt(string PromptId, string UseCase, int Version, string SystemInstruction, string UserMessage);
