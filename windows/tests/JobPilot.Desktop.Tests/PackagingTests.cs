using System.Xml.Linq;
using JobPilot.Desktop.Core;

namespace JobPilot.Desktop.Tests;

/// <summary>The host project, the installer script and the CI workflow agree on names the window relies on.</summary>
public sealed class PackagingTests
{
    private static readonly string Repo = FindRepo();

    private static string FindRepo()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "JobPilot.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Repo, .. parts]));

    [Fact]
    public void The_host_executable_has_the_name_the_installer_starts()
    {
        var project = XDocument.Parse(Read("windows", "src", "JobPilot.Desktop", "JobPilot.Desktop.csproj"));
        var assemblyName = project.Descendants("AssemblyName").Single().Value;
        var script = Read("windows", "installer", "JobSearchCopilot.iss");

        Assert.Equal(DesktopIdentity.ExecutableName, assemblyName + ".exe");
        Assert.Contains($"#define AppExe \"{DesktopIdentity.ExecutableName}\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_installer_waits_for_the_window_and_replaces_the_server_folder()
    {
        var script = Read("windows", "installer", "JobSearchCopilot.iss");

        Assert.Contains($"AppMutex={DesktopIdentity.MutexName}", script, StringComparison.Ordinal);
        Assert.Contains($"Name: \"{{app}}\\{DesktopIdentity.ServerFolder}\"", script, StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=lowest", script, StringComparison.Ordinal);
        // The shared data folder is never deleted by the uninstaller.
        Assert.DoesNotContain("[UninstallDelete]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_workflow_publishes_the_web_app_into_the_server_folder()
    {
        var workflow = Read(".github", "workflows", "windows.yml");

        Assert.Contains($"/{DesktopIdentity.ServerFolder}\"", workflow.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("src/JobPilot.Web", workflow, StringComparison.Ordinal);
        Assert.Contains("windows/installer/JobSearchCopilot.iss", workflow.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void The_web_app_still_serves_the_script_the_probe_looks_for()
    {
        var script = Read("src", "JobPilot.Web", "wwwroot", "app.js");

        Assert.Contains(ServerProbe.Marker, script, StringComparison.Ordinal);
    }
}
