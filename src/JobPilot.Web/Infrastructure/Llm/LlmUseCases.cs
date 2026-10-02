namespace JobPilot.Web.Infrastructure.Llm;

public sealed record LlmUseCaseInfo(string Name, string Label, string Description);

/// <summary>Catalog of implemented LLM use cases. Each one has a prompt file <c>Prompts/&lt;name&gt;.v&lt;N&gt;.md</c>.</summary>
public static class LlmUseCases
{
    public const string ConnectionTest = "connection-test";

    public const string CvImport = "cv-import";

    public const string JobExtraction = "job-extraction";

    public const string JobAnalysis = "job-analysis";

    public const string CvTailoring = "cv-tailoring";

    public const string CoverLetter = "cover-letter";

    public const string FollowUpEmail = "follow-up-email";

    public const string CompanyResearch = "company-research";

    public const string OutreachMessage = "outreach-message";

    public const string LinkedInReview = "linkedin-review";

    public const string InterviewQuestions = "interview-questions";

    public const string StarStories = "star-stories";

    public const string MockInterview = "mock-interview";

    public const string NegotiationRoleplay = "negotiation-roleplay";

    public const string PracticeFeedback = "practice-feedback";

    public const string NegotiationScript = "negotiation-script";

    public static IReadOnlyList<LlmUseCaseInfo> All { get; } =
    [
        new(ConnectionTest, "Prueba de conexión", "Verifica la API key, el modelo y la salida estructurada con un pedido mínimo."),
        new(CvImport, "Importación del CV", "Convierte el texto de tu CV en el modelo estructurado del CV maestro, sin agregar información."),
        new(JobExtraction, "Alta manual de ofertas", "Extrae los campos de una oferta pegada a mano; el salario solo se toma si está citado textualmente."),
        new(CvTailoring, "CV a medida", "Reordena y reformula tu CV maestro para una oferta; cada viñeta cita sus fuentes y se verifica."),
        new(CoverLetter, "Carta de presentación", "Redacta la carta para una oferta a partir de tu CV maestro; cada párrafo cita sus fuentes."),
        new(FollowUpEmail, "Follow-up de postulaciones", "Redacta un e-mail breve de seguimiento para que lo revises, copies y envíes vos; no inventa fechas ni datos."),
        new(CompanyResearch, "Fichas de empresas", "Arma la ficha con las URLs que elegiste (Gemini las lee) y textos pegados; cada dato cita su fuente y se verifica."),
        new(OutreachMessage, "Mensajes de outreach", "Borradores para recruiters y hiring managers en tres tonos; solo se copian, la app no envía nada."),
        new(LinkedInReview, "Revisión de LinkedIn", "Revisa tu titular y tu \"Acerca de\" y propone versiones basadas en tu CV maestro."),
        new(InterviewQuestions, "Banco de preguntas", "Preguntas probables para una oferta, cada una ligada a la parte de la oferta que la motiva."),
        new(StarStories, "Historias STAR", "Historias STAR armadas solo con tu CV maestro; cada parte cita su fuente."),
        new(MockInterview, "Simulacro de entrevista", "El entrevistador en un chat: una pregunta por vez, repreguntas acotadas."),
        new(NegotiationRoleplay, "Simulacro de negociación", "El recruiter en un chat, a partir de la oferta que cargaste."),
        new(PracticeFeedback, "Feedback de simulacros", "Puntaje por rúbrica y respuestas modelo basadas en tu CV."),
        new(NegotiationScript, "Guion de negociación", "Guion para negociar una oferta con tus números y tu CV; los números se verifican."),
        new(JobAnalysis, "Análisis de ofertas", "Compara la oferta con tu perfil y CV maestro: encaje, requisitos, compensación publicada, modalidad, elegibilidad y alertas."),
    ];
}
