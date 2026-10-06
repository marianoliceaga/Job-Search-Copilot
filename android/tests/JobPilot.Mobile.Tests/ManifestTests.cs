using System.Xml.Linq;

namespace JobPilot.Mobile.Tests;

/// <summary>What the APK asks Android for: only internet access, no backup of the app's data to the cloud.</summary>
public class ManifestTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    private static XDocument Manifest() =>
        XDocument.Load(Path.Combine(AndroidFolder.Root, "src", "JobPilot.Mobile", "Platforms", "Android", "AndroidManifest.xml"));

    [Fact]
    public void Only_network_permissions_are_requested()
    {
        var permissions = Manifest().Root!.Elements("uses-permission").Select(e => (string)e.Attribute(Android + "name")!).ToList();

        Assert.Equal(["android.permission.ACCESS_NETWORK_STATE", "android.permission.INTERNET"], permissions.Order());
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
