using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using JobPilot.Mobile.Core.Infrastructure.Notifications;
using JobPilot.Mobile.Platforms.Android.Notifications;

namespace JobPilot.Mobile;

// SingleTop: tapping a notification while the app is open reuses this activity (OnNewIntent) instead of starting another.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OpenRequestedPage(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        OpenRequestedPage(intent);
    }

    /// <summary>A tapped notification carries the page to open; <see cref="AppNavigationRequests"/> accepts only app routes.</summary>
    private static void OpenRequestedPage(Intent? intent)
    {
        var route = intent?.GetStringExtra(AndroidNotificationScheduler.RouteExtra);
        if (route is not null && IPlatformApplication.Current?.Services.GetService<AppNavigationRequests>() is { } requests)
        {
            requests.Request(route);
        }
    }
}
