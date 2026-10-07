using System.Text.Json;
using System.Text.Json.Serialization;
using Android.App;
using Android.Content;
using JobPilot.Mobile.Core.Infrastructure.Notifications;

namespace JobPilot.Mobile.Platforms.Android.Notifications;

/// <summary>
/// Local notifications with <see cref="AlarmManager"/>: exact alarms when the owner allowed them (Android 12+ asks for
/// "Alarmas y recordatorios"), otherwise inexact ones that Android may delay. The schedule is kept in a private file so
/// <see cref="RescheduleReceiver"/> can restore it after a reboot (Android cancels every alarm on shutdown).
/// </summary>
public sealed class AndroidNotificationScheduler : INotificationScheduler
{
    public const string ChannelId = "jobpilot-reminders";
    public const string ShowAction = "com.vidi.jobpilot.SHOW_REMINDER";
    public const string RouteExtra = "jobpilot.route";

    private static readonly Lock FileLock = new();

    private static Context AppContext => global::Android.App.Application.Context;

    private static string SchedulePath => Path.Combine(AppContext.FilesDir!.AbsolutePath, "scheduled-notifications.json");

    public Task<NotificationAccess> GetAccessAsync()
    {
        var notifications = (NotificationManager)AppContext.GetSystemService(Context.NotificationService)!;
        var needsPermission = OperatingSystem.IsAndroidVersionAtLeast(31);
        return Task.FromResult(new NotificationAccess(notifications.AreNotificationsEnabled(), CanUseExactAlarms(), needsPermission));
    }

    public async Task<bool> RequestNotificationPermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<Permissions.PostNotifications>);
        }

        return (await GetAccessAsync()).CanNotify;
    }

    public void OpenExactAlarmSettings()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return;
        }

        var intent = new Intent(global::Android.Provider.Settings.ActionRequestScheduleExactAlarm, global::Android.Net.Uri.Parse("package:" + AppContext.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        AppContext.StartActivity(intent);
    }

    public Task ReplaceAsync(IReadOnlyList<ScheduledNotification> notifications)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        EnsureChannel(AppContext);
        lock (FileLock)
        {
            foreach (var previous in Load())
            {
                Cancel(previous.Id);
            }

            Save(notifications);
            Schedule(notifications);
        }

        return Task.CompletedTask;
    }

    /// <summary>Schedules the stored notifications again (after a reboot or when exact alarms were allowed).</summary>
    public static void RestoreSchedule()
    {
        lock (FileLock)
        {
            var now = DateTime.UtcNow;
            Schedule(Load().Where(n => n.AtUtc > now).ToList());
        }
    }

    public static void EnsureChannel(Context context)
    {
        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        var channel = new NotificationChannel(ChannelId, "Recordatorios y entrevistas", NotificationImportance.High)
        {
            Description = "Follow-ups que vencen y entrevistas próximas del pipeline.",
            LockscreenVisibility = NotificationVisibility.Private,
        };
        manager.CreateNotificationChannel(channel);
    }

    private static bool CanUseExactAlarms()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            return true;
        }

        var alarms = (AlarmManager)AppContext.GetSystemService(Context.AlarmService)!;
        return alarms.CanScheduleExactAlarms();
    }

    private static void Schedule(IReadOnlyList<ScheduledNotification> notifications)
    {
        var alarms = (AlarmManager)AppContext.GetSystemService(Context.AlarmService)!;
        var exact = CanUseExactAlarms();
        foreach (var notification in notifications)
        {
            var at = new DateTimeOffset(DateTime.SpecifyKind(notification.AtUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
            var pending = AlarmIntent(notification);
            if (exact)
            {
                alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
            }
            else
            {
                alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, at, pending);
            }
        }
    }

    private static void Cancel(int id)
    {
        var alarms = (AlarmManager)AppContext.GetSystemService(Context.AlarmService)!;
        var pending = PendingIntent.GetBroadcast(AppContext, id, BaseIntent(), PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pending is not null)
        {
            alarms.Cancel(pending);
            pending.Cancel();
        }
    }

    /// <summary>Alarms are matched by request code and action, not by extras.</summary>
    private static Intent BaseIntent() => new Intent(AppContext, typeof(ReminderReceiver)).SetAction(ShowAction);

    private static PendingIntent AlarmIntent(ScheduledNotification notification)
    {
        var intent = BaseIntent()
            .PutExtra("id", notification.Id)
            .PutExtra("title", notification.Title)
            .PutExtra("text", notification.Text)
            .PutExtra(RouteExtra, notification.Route);
        return PendingIntent.GetBroadcast(AppContext, notification.Id, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    private static List<ScheduledNotification> Load()
    {
        try
        {
            return File.Exists(SchedulePath)
                ? JsonSerializer.Deserialize(File.ReadAllText(SchedulePath), ScheduleJson.Default.ListScheduledNotification) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private static void Save(IReadOnlyList<ScheduledNotification> notifications)
    {
        if (notifications.Count == 0)
        {
            File.Delete(SchedulePath);
            return;
        }

        File.WriteAllText(SchedulePath, JsonSerializer.Serialize(notifications.ToList(), ScheduleJson.Default.ListScheduledNotification));
    }
}

/// <summary>Source-generated JSON for the stored schedule (no reflection, safe with trimming).</summary>
[JsonSerializable(typeof(List<ScheduledNotification>))]
internal sealed partial class ScheduleJson : JsonSerializerContext;
