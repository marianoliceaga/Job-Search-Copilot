using System.Globalization;
using System.Net;
using JobPilot.Web.Infrastructure.Google;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace JobPilot.Web.Features.Google;

/// <summary>Confirmation dialogs that show exactly what will be sent to Google before it is sent.</summary>
public static class GoogleUi
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");

    public static Task<bool?> ConfirmDraftAsync(IDialogService dialogs, DraftRequest draft)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(draft);
        var html = $"<p>Se crea un <strong>borrador</strong> en tu Gmail. JobSearch Copilot no lo envía: lo revisás y lo enviás vos desde Gmail.</p>" +
                   $"<pre class=\"jp-payload\">Para: {Encode(string.IsNullOrWhiteSpace(draft.To) ? "(vacío: lo completás en Gmail)" : draft.To)}\n" +
                   $"Asunto: {Encode(draft.Subject)}\n" +
                   (draft.ThreadId is null ? string.Empty : "Como respuesta en el hilo vinculado\n") +
                   $"\n{Encode(draft.Body)}</pre>";
        return dialogs.ShowMessageBoxAsync("Crear borrador en Gmail", (MarkupString)html, yesText: "Crear borrador", cancelText: "Cancelar");
    }

    public static Task<bool?> ConfirmEventAsync(IDialogService dialogs, CalendarEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(request);
        var html = "<p>Se crea este evento en el calendario <strong>JobSearch Copilot</strong> de tu Google Calendar (no en tu calendario principal).</p>" +
                   $"<pre class=\"jp-payload\">{Encode(request.Summary)}\n" +
                   $"{request.StartUtc.ToLocalTime().ToString("dddd dd/MM/yyyy HH:mm", Culture)} – {request.EndUtc.ToLocalTime().ToString("HH:mm", Culture)}\n\n" +
                   $"{Encode(request.Description ?? string.Empty)}</pre>";
        return dialogs.ShowMessageBoxAsync("Agregar a Google Calendar", (MarkupString)html, yesText: "Crear evento", cancelText: "Cancelar");
    }

    public static string ScopeLabel(string scope) => scope switch
    {
        GoogleScopes.GmailReadonly => "Leer tus correos (para vincular y leer hilos de una postulación)",
        GoogleScopes.GmailCompose => "Crear borradores (Google lo llama \"administrar borradores y enviar\"; JobSearch Copilot nunca envía)",
        GoogleScopes.CalendarAppCreated => "Crear el calendario \"JobPilot\" y manejar solo sus eventos",
        _ => scope,
    };

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
