using JobPilot.Mobile.Core.Layout;

namespace JobPilot.Mobile.Tests;

public class ArchitectureTests
{
    [Fact]
    public void The_core_library_does_not_reference_maui()
    {
        // Platform services go behind interfaces implemented in JobPilot.Mobile, so the core stays testable on net10.0.
        var references = typeof(AppSections).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(references, r => r.StartsWith("Microsoft.Maui", StringComparison.Ordinal) || r.StartsWith("Mono.Android", StringComparison.Ordinal));
    }

    [Fact]
    public void The_app_reuses_the_web_domain_project_without_a_copy()
    {
        var root = AndroidFolder.Root;
        var project = File.ReadAllText(Path.Combine(root, "src", "JobPilot.Mobile.Core", "JobPilot.Mobile.Core.csproj"));

        Assert.Contains(@"..\..\..\src\JobPilot.Domain\JobPilot.Domain.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain(AndroidFolder.SourceFiles(".cs"), f => File.ReadAllText(f).Contains("namespace JobPilot.Domain", StringComparison.Ordinal));
    }
}
