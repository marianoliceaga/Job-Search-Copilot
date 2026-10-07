using JobPilot.Domain.Companies;

namespace JobPilot.Mobile.Core.Features.Companies;

public static class CompanyFormat
{
    public static string Category(CompanyFactCategory category) => category switch
    {
        CompanyFactCategory.Overview => "General",
        CompanyFactCategory.Product => "Producto",
        CompanyFactCategory.Size => "Tamaño y ubicaciones",
        CompanyFactCategory.Funding => "Financiamiento",
        CompanyFactCategory.Technology => "Tecnología",
        CompanyFactCategory.Culture => "Cultura",
        CompanyFactCategory.RemoteWork => "Trabajo remoto",
        CompanyFactCategory.Hiring => "Proceso de selección",
        CompanyFactCategory.News => "Novedades",
        _ => "Otros",
    };

    public static string Verification(CompanyFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return fact.Verification switch
        {
            FactVerification.QuoteFound => "Verificado: la cita está en el texto que pegaste",
            FactVerification.UrlRead => "Verificado: Gemini leyó la URL citada (la cita no se comprobó localmente)",
            _ => "No verificado: " + fact.VerificationNote,
        };
    }

    public static string RetrievalStatus(string status) => status switch
    {
        "URL_RETRIEVAL_STATUS_SUCCESS" => "leída",
        "URL_RETRIEVAL_STATUS_PAYWALL" => "con paywall",
        "URL_RETRIEVAL_STATUS_UNSAFE" => "bloqueada por seguridad",
        "URL_RETRIEVAL_STATUS_ERROR" => "error al leerla",
        _ => "estado desconocido",
    };

    /// <summary>A Google search in the owner's browser, to find pages to add as sources (the app does not search).</summary>
    public static string GoogleSearch(string company) => "https://www.google.com/search?q=" + Uri.EscapeDataString(company);
}
