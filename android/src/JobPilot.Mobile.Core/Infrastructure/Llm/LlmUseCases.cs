namespace JobPilot.Mobile.Core.Infrastructure.Llm;

public sealed record LlmUseCaseInfo(string Name, string Label, string Description);

/// <summary>
/// LLM use cases implemented in the Android app, with the same names, labels and prompts as the web app's catalog.
/// Each phase adds its own; the prompts come from the web app (<c>src/JobPilot.Web/Prompts</c>, embedded).
/// </summary>
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
