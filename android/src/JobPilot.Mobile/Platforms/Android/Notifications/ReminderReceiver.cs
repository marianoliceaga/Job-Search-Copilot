using Android.App;
using Android.Content;

namespace JobPilot.Mobile.Platforms.Android.Notifications;

/// <summary>
/// Shows a scheduled notification when its alarm goes off. The lock screen shows only a generic text (agreed with the
/// owner); the offer appears once the phone is unlocked. Tapping it opens the application's page.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class ReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != AndroidNotificationScheduler.ShowAction)
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (!manager.AreNotificationsEnabled())
        {
            return;
        }

        AndroidNotificationScheduler.EnsureChannel(context);
        var id = intent.GetIntExtra("id", 0);
        var open = new Intent(context, typeof(MainActivity))
            .PutExtra(AndroidNotificationScheduler.RouteExtra, intent.GetStringExtra(AndroidNotificationScheduler.RouteExtra))
            .AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var tap = PendingIntent.GetActivity(context, id, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var generic = new Notification.Builder(context, AndroidNotificationScheduler.ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_stat_jobpilot)
            .SetContentTitle("JobPilot")
            .SetContentText("Tenés un recordatorio.")
            .Build();
        var text = intent.GetStringExtra("text") ?? string.Empty;
        var notification = new Notification.Builder(context, AndroidNotificationScheduler.ChannelId)
            .SetSmallIcon(Resource.Drawable.ic_stat_jobpilot)
            .SetContentTitle(intent.GetStringExtra("title"))
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetCategory(Notification.CategoryReminder)
            .SetVisibility(NotificationVisibility.Private)
            .SetPublicVersion(generic)
            .SetContentIntent(tap)
            .SetAutoCancel(true)
            .Build();
        manager.Notify(id, notification);
    }
}

/// <summary>
/// Restores the alarms after a reboot or an app update, and makes them exact once the owner allows exact alarms. The app
/// also schedules them again on every start.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter([Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced, RescheduleReceiver.ExactAlarmPermissionChanged])]
public sealed class RescheduleReceiver : BroadcastReceiver
{
    /// <summary><c>AlarmManager.ACTION_SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED</c> (Android 12+; never sent before).</summary>
    public const string ExactAlarmPermissionChanged = "android.app.action.SCHEDULE_EXACT_ALARM_PERMISSION_STATE_CHANGED";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action is Intent.ActionBootCompleted or Intent.ActionMyPackageReplaced or ExactAlarmPermissionChanged)
        {
            AndroidNotificationScheduler.RestoreSchedule();
        }
    }
}
