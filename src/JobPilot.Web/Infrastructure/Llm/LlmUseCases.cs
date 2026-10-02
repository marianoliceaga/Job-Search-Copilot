namespace JobPilot.Web.Infrastructure.Llm;

public sealed record LlmUseCaseInfo(string Name, string Label, string Description);

/// <summary>Catalog of implemented LLM use cases. Each one has a prompt file <c>Prompts/&lt;name&gt;.v&lt;N&gt;.md</c>.</summary>
public static class LlmUseCases
{
    public const string ConnectionTest = "connection-test";

    public const string CvImport = "cv-import";

    public const string JobExtraction = "job-extraction";

    public const string JobAnalysis = "job-analysis";

    public static IReadOnlyList<LlmUseCaseInfo> All { get; } =
    [
        new(ConnectionTest, "Prueba de conexión", "Verifica la API key, el modelo y la salida estructurada con un pedido mínimo."),
        new(CvImport, "Importación del CV", "Convierte el texto de tu CV en el modelo estructurado del CV maestro, sin agregar información."),
        new(JobExtraction, "Alta manual de ofertas", "Extrae los campos de una oferta pegada a mano; el salario solo se toma si está citado textualmente."),
        new(JobAnalysis, "Análisis de ofertas", "Compara la oferta con tu perfil y CV maestro: encaje, requisitos, compensación publicada, modalidad, elegibilidad y alertas."),
    ];
}
