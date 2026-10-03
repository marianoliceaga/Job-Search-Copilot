namespace JobPilot.Domain.Llm;

/// <summary>
/// Owner override, edited in the UI, of the configured model parameters for a use case.
/// Null fields fall back to configuration.
/// </summary>
public sealed class LlmUseCaseSetting
{
    public required string UseCase { get; set; }

    public string? Model { get; set; }

    public string? ThinkingLevel { get; set; }

    public double? Temperature { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
