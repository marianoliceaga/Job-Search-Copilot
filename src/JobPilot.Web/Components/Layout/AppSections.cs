using MudBlazor;

namespace JobPilot.Web.Components.Layout;

/// <summary>A top-level section of the app, shown in the navigation menu.</summary>
/// <param name="Phase">Delivery phase (see docs/PLAN.md) in which the section becomes functional.</param>
public sealed record AppSection(string Title, string Href, string Icon, int Phase, string Description);

/// <summary>Single source of truth for the navigation menu and section placeholders.</summary>
public static class AppSections
{
    public static readonly AppSection Dashboard = new(
        "Panel", "/", Icons.Material.Filled.Dashboard, 5,
        "Métricas de ofertas, postulaciones, tasa de respuesta, tiempos por etapa y consumo del LLM.");

    public static readonly AppSection Profile = new(
        "Perfil y CV maestro", "/perfil", Icons.Material.Filled.Person, 2,
        "Importación del CV (PDF, DOCX o Markdown) a un modelo estructurado y edición de cada elemento.");

    public static readonly AppSection Jobs = new(
        "Ofertas", "/ofertas", Icons.Material.Filled.Work, 3,
        "Búsqueda en fuentes permitidas, alta manual, filtros, análisis, ranking, y CV y carta a medida.");

    public static readonly AppSection Pipeline = new(
        "Pipeline", "/pipeline", Icons.Material.Filled.ViewKanban, 5,
        "Tablero Kanban, línea de tiempo, notas, contactos, recordatorios de follow-up y exportación .ics.");

    public static readonly AppSection Companies = new(
        "Empresas", "/empresas", Icons.Material.Filled.Business, 6,
        "Fichas de empresas armadas con las URLs y textos que elegís; cada dato cita su fuente y los no verificados se marcan.");

    public static readonly AppSection Outreach = new(
        "Outreach", "/outreach", Icons.Material.Filled.Send, 6,
        "Borradores para recruiters y hiring managers, y revisión del perfil de LinkedIn. Solo copiar; la app no envía nada.");

    public static readonly AppSection Interviews = new(
        "Entrevistas", "/entrevistas", Icons.Material.Filled.RecordVoiceOver, 7,
        "Banco de preguntas por oferta, historias STAR basadas en tu CV y simulacros en inglés con feedback por rúbrica.");

    public static readonly AppSection Negotiation = new(
        "Negociación", "/negociacion", Icons.Material.Filled.Handshake, 7,
        "Comparador de ofertas en USD anuales con tus tipos de cambio y costos, guion de negociación y simulacro con el recruiter.");

    public static readonly AppSection Settings = new(
        "Configuración", "/configuracion", Icons.Material.Filled.Settings, 1,
        "Modelos de Gemini por caso de uso, estado de la API key y registro de llamadas al LLM.");

    public static IReadOnlyList<AppSection> All { get; } =
    [
        Dashboard, Profile, Jobs, Pipeline, Companies, Outreach, Interviews, Negotiation, Settings,
    ];
}
