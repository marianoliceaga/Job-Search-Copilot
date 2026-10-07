using JobPilot.Mobile.Core.Infrastructure.Notifications;

namespace JobPilot.Mobile.Tests.TestSupport;

/// <summary>Stand-in for Android's alarms and notification permission: records the last schedule.</summary>
public sealed class FakeNotificationScheduler : INotificationScheduler
{
    public NotificationAccess Access { get; set; } = new(CanNotify: true, CanUseExactAlarms: true, ExactAlarmsNeedPermission: true);

    /// <summary>What the owner answers when Android asks for the notification permission.</summary>
    public bool GrantOnRequest { get; set; } = true;

    public int PermissionRequests { get; private set; }

    public int ExactAlarmSettingsOpened { get; private set; }

    public IReadOnlyList<ScheduledNotification>? Scheduled { get; private set; }

    public int Replacements { get; private set; }

    public Task<NotificationAccess> GetAccessAsync() => Task.FromResult(Access);

    public Task<bool> RequestNotificationPermissionAsync()
    {
        PermissionRequests++;
        Access = Access with { CanNotify = GrantOnRequest };
        return Task.FromResult(GrantOnRequest);
    }

    public void OpenExactAlarmSettings() => ExactAlarmSettingsOpened++;

    public Task ReplaceAsync(IReadOnlyList<ScheduledNotification> notifications)
    {
        Scheduled = notifications;
        Replacements++;
        return Task.CompletedTask;
    }
}
