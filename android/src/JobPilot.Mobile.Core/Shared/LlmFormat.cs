using System.Globalization;
using JobPilot.Domain.Llm;

namespace JobPilot.Mobile.Core.Shared;

/// <summary>UI formatting helpers for LLM usage and cost.</summary>
public static class LlmFormat
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");

    public static string Cost(decimal? usd) =>
        usd is { } value ? "US$ " + value.ToString("0.000000", Culture) : "sin precio configurado";

    public static string Tokens(long tokens) => tokens.ToString("N0", Culture);

    public static string Usage(LlmUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var text = $"{Tokens(usage.InputTokens)} de entrada · {Tokens(usage.OutputTokens)} de salida";
        return usage.ThoughtsTokens > 0 ? text + $" · {Tokens(usage.ThoughtsTokens)} de razonamiento" : text;
    }

    public static string Latency(long milliseconds) =>
        milliseconds < 1000 ? $"{milliseconds} ms" : (milliseconds / 1000d).ToString("0.0", Culture) + " s";

    public static string ErrorKind(LlmErrorKind? kind) => kind switch
    {
        LlmErrorKind.Configuration => "Configuración",
        LlmErrorKind.Authentication => "API key rechazada",
        LlmErrorKind.InvalidRequest => "Pedido inválido",
        LlmErrorKind.ModelNotFound => "Modelo inexistente",
        LlmErrorKind.RateLimited => "Límite de uso",
        LlmErrorKind.ServerError => "Error de Gemini",
        LlmErrorKind.Timeout => "Sin respuesta a tiempo",
        LlmErrorKind.Network => "Sin conexión",
        LlmErrorKind.Blocked => "Bloqueado",
        LlmErrorKind.Truncated => "Respuesta cortada",
        LlmErrorKind.InvalidOutput => "Respuesta inválida",
        LlmErrorKind.Cancelled => "Cancelada",
        _ => "Error",
    };

    public static string LocalTime(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", Culture);
}
