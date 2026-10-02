using System.Globalization;
using JobPilot.Domain.Pipeline;

namespace JobPilot.Web.Features.Pipeline;

/// <summary>UI labels for the pipeline (Spanish) and the English names used in prompts.</summary>
public static class PipelineFormat
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-AR");

    public static string Label(ApplicationStage stage) => stage switch
    {
        ApplicationStage.Detected => "Detectada",
        ApplicationStage.Interested => "Interesa",
        ApplicationStage.Applied => "Postulada",
        ApplicationStage.Interview => "Entrevista",
        ApplicationStage.Offer => "Oferta",
        ApplicationStage.Rejected => "Rechazada",
        ApplicationStage.Discarded => "Descartada",
        _ => stage.ToString(),
    };

    public static string English(ApplicationStage stage) => stage switch
    {
        ApplicationStage.Detected => "Detected (not applied yet)",
        ApplicationStage.Interested => "Interested (not applied yet)",
        ApplicationStage.Applied => "Applied",
        ApplicationStage.Interview => "Interviewing",
        ApplicationStage.Offer => "Offer received",
        ApplicationStage.Rejected => "Rejected",
        ApplicationStage.Discarded => "Discarded by the candidate",
        _ => stage.ToString(),
    };

    public static MudBlazor.Color Color(ApplicationStage stage) => stage switch
    {
        ApplicationStage.Applied => MudBlazor.Color.Info,
        ApplicationStage.Interview => MudBlazor.Color.Primary,
        ApplicationStage.Offer => MudBlazor.Color.Success,
        ApplicationStage.Rejected => MudBlazor.Color.Error,
        _ => MudBlazor.Color.Default,
    };

    public static string Describe(ApplicationEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            ApplicationEventKind.Created => $"Agregada al pipeline en «{Label(item.ToStage ?? ApplicationStage.Detected)}»",
            ApplicationEventKind.StageChanged => $"{Label(item.FromStage!.Value)} → {Label(item.ToStage!.Value)}",
            ApplicationEventKind.Note => "Nota",
            ApplicationEventKind.ContactAdded => "Contacto agregado",
            ApplicationEventKind.DocumentSent => "Documento enviado",
            ApplicationEventKind.InterviewScheduled => $"Entrevista agendada para el {LocalDateTime(item.ScheduledAtUtc)} ({item.DurationMinutes} min)",
            ApplicationEventKind.FollowUpDrafted => "Borrador de follow-up generado",
            ApplicationEventKind.FollowUpSent => "Follow-up enviado (por vos)",
            ApplicationEventKind.ResponseReceived => "Respuesta recibida",
            ApplicationEventKind.MessageSent => "Mensaje de outreach enviado (por vos)",
            ApplicationEventKind.ReminderSet => item.ScheduledAtUtc is null ? "Recordatorio personalizado quitado" : $"Recordatorio para el {Date(item.ScheduledAtUtc)}",
            _ => item.Kind.ToString(),
        };
    }

    public static string Icon(ApplicationEventKind kind) => kind switch
    {
        ApplicationEventKind.Created => MudBlazor.Icons.Material.Filled.AddCircleOutline,
        ApplicationEventKind.StageChanged => MudBlazor.Icons.Material.Filled.SwapHoriz,
        ApplicationEventKind.Note => MudBlazor.Icons.Material.Filled.StickyNote2,
        ApplicationEventKind.ContactAdded => MudBlazor.Icons.Material.Filled.PersonAdd,
        ApplicationEventKind.DocumentSent => MudBlazor.Icons.Material.Filled.Description,
        ApplicationEventKind.InterviewScheduled => MudBlazor.Icons.Material.Filled.Event,
        ApplicationEventKind.FollowUpDrafted => MudBlazor.Icons.Material.Filled.EditNote,
        ApplicationEventKind.FollowUpSent => MudBlazor.Icons.Material.Filled.MarkEmailRead,
        ApplicationEventKind.ResponseReceived => MudBlazor.Icons.Material.Filled.MarkEmailUnread,
        ApplicationEventKind.MessageSent => MudBlazor.Icons.Material.Filled.Send,
        _ => MudBlazor.Icons.Material.Filled.Alarm,
    };

    public static string ReminderText(FollowUpReminder reminder, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        var what = reminder.Kind switch
        {
            ReminderKind.NoResponse => "Revisar",
            ReminderKind.Custom => "Recordatorio",
            _ => "Follow-up",
        };
        if (!reminder.IsDue(nowUtc))
        {
            return $"{what} el {Date(reminder.DueAtUtc)}";
        }

        var days = reminder.DaysOverdue(nowUtc);
        return days == 0 ? $"{what}: hoy" : $"{what}: vencido hace {days} día(s)";
    }

    public static string Date(DateTime? utc) => utc?.ToLocalTime().ToString("dd/MM/yyyy", Culture) ?? "—";

    public static string LocalDateTime(DateTime? utc) => utc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", Culture) ?? "—";

    public static string Days(double? days) => days is { } value ? value.ToString("0.0", Culture) + " días" : "—";

    public static string Percent(double? rate) => rate is { } value ? value.ToString("P0", Culture) : "—";
}
