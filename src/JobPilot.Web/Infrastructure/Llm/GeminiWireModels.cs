namespace JobPilot.Web.Infrastructure.Llm;

// Subset of the Gemini REST API (v1beta generateContent / models.list) read by the client.
// Field names verified against the API discovery document on 2026-10-02.

internal sealed record GeminiGenerateContentResponse(
    List<GeminiCandidate>? Candidates,
    GeminiPromptFeedback? PromptFeedback,
    GeminiUsageMetadata? UsageMetadata,
    string? ModelVersion,
    string? ResponseId);

internal sealed record GeminiCandidate(GeminiContent? Content, string? FinishReason);

internal sealed record GeminiContent(string? Role, List<GeminiPart>? Parts);

internal sealed record GeminiPart(string? Text, bool? Thought);

internal sealed record GeminiPromptFeedback(string? BlockReason);

internal sealed record GeminiUsageMetadata(
    int PromptTokenCount,
    int CandidatesTokenCount,
    int ThoughtsTokenCount,
    int CachedContentTokenCount,
    int ToolUsePromptTokenCount,
    int TotalTokenCount);

internal sealed record GeminiErrorEnvelope(GeminiErrorBody? Error);

internal sealed record GeminiErrorBody(int Code, string? Message, string? Status, List<GeminiErrorDetail>? Details);

internal sealed record GeminiErrorDetail(string? Reason);

internal sealed record GeminiListModelsResponse(List<GeminiModel>? Models, string? NextPageToken);

internal sealed record GeminiModel(string Name, string? DisplayName, List<string>? SupportedGenerationMethods);
