namespace JobPilot.Mobile.Core.Infrastructure.Llm;

public sealed record LlmUseCaseInfo(string Name, string Label, string Description);

/// <summary>
/// LLM use cases implemented in the Android app, with the same names, labels and prompts as the web app's catalog.
/// Each phase adds its own; the prompts come from the web app (<c>src/JobPilot.Web/Prompts</c>, embedded).
/// </summary>
public static class LlmUseCases
{
    public const string ConnectionTest = "connection-test";

    public static IReadOnlyList<LlmUseCaseInfo> All { get; } =
    [
        new(ConnectionTest, "Prueba de conexión", "Verifica la API key, el modelo y la salida estructurada con un pedido mínimo."),
    ];
}
