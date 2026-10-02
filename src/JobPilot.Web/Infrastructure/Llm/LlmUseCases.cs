namespace JobPilot.Web.Infrastructure.Llm;

public sealed record LlmUseCaseInfo(string Name, string Label, string Description);

/// <summary>Catalog of implemented LLM use cases. Each one has a prompt file <c>Prompts/&lt;name&gt;.v&lt;N&gt;.md</c>.</summary>
public static class LlmUseCases
{
    public const string ConnectionTest = "connection-test";

    public const string CvImport = "cv-import";

    public static IReadOnlyList<LlmUseCaseInfo> All { get; } =
    [
        new(ConnectionTest, "Prueba de conexión", "Verifica la API key, el modelo y la salida estructurada con un pedido mínimo."),
        new(CvImport, "Importación del CV", "Convierte el texto de tu CV en el modelo estructurado del CV maestro, sin agregar información."),
    ];
}
