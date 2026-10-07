namespace JobPilot.Mobile.Core.Infrastructure.Notifications;

/// <summary>A local notification at a given time. Nothing leaves the phone: Android shows it to the owner.</summary>
/// <param name="Id">Notification and alarm id, unique within one schedule.</param>
/// <param name="Route">Page of the app opened when the owner taps it (<see cref="AppNavigationRequests.IsAllowed"/>).</param>
public sealed record ScheduledNotification(int Id, DateTime AtUtc, string Title, string Text, string Route);

/// <param name="CanNotify">The owner allowed notifications (always true before Android 13).</param>
/// <param name="CanUseExactAlarms">Alarms ring on time; otherwise Android may delay them (always true before Android 12).</param>
/// <param name="ExactAlarmsNeedPermission">The owner can allow exact alarms in the system settings (Android 12+).</param>
public sealed record NotificationAccess(bool CanNotify, bool CanUseExactAlarms, bool ExactAlarmsNeedPermission);

/// <summary>Local notifications through Android's alarms (implemented by the host).</summary>
public interface INotificationScheduler
{
    Task<NotificationAccess> GetAccessAsync();

    /// <summary>Asks the owner to allow notifications (Android 13+). Returns whether they are allowed.</summary>
    Task<bool> RequestNotificationPermissionAsync();

    /// <summary>Opens the system screen where the owner allows exact alarms ("Alarmas y recordatorios").</summary>
    void OpenExactAlarmSettings();

    /// <summary>Cancels every notification scheduled before and schedules these ones (an empty list cancels all).</summary>
    Task ReplaceAsync(IReadOnlyList<ScheduledNotification> notifications);
}
