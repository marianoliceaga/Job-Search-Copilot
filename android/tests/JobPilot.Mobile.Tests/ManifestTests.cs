using System.Xml.Linq;

namespace JobPilot.Mobile.Tests;

/// <summary>What the APK asks Android for: internet, file reading on old Android versions only, no cloud backup.</summary>
public class ManifestTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    private static XDocument Manifest() =>
        XDocument.Load(Path.Combine(AndroidFolder.Root, "src", "JobPilot.Mobile", "Platforms", "Android", "AndroidManifest.xml"));

    [Fact]
    public void Only_network_permissions_and_legacy_file_reading_are_requested()
    {
        var permissions = Manifest().Root!.Elements("uses-permission")
            .ToDictionary(e => (string)e.Attribute(Android + "name")!, e => (string?)e.Attribute(Android + "maxSdkVersion"));

        Assert.Equal(["android.permission.ACCESS_NETWORK_STATE", "android.permission.INTERNET", "android.permission.READ_EXTERNAL_STORAGE"], permissions.Keys.Order());
        // The file picker needs it only up to Android 12 (API 32); newer versions use the document picker without it.
        Assert.Equal("32", permissions["android.permission.READ_EXTERNAL_STORAGE"]);
    }

    [Fact]
    public void App_data_is_not_backed_up_to_the_cloud()
    {
        Assert.Equal("false", (string?)Manifest().Root!.Element("application")!.Attribute(Android + "allowBackup"));
    }

    [Fact]
    public void The_app_targets_android_8_or_later_with_a_fixed_id()
    {
        var project = XDocument.Load(Path.Combine(AndroidFolder.Root, "src", "JobPilot.Mobile", "JobPilot.Mobile.csproj"));
        string Property(string name) => project.Descendants(name).Single().Value;

        Assert.Equal("26.0", Property("SupportedOSPlatformVersion"));
        Assert.Equal("com.vidi.jobpilot", Property("ApplicationId"));
    }
}
