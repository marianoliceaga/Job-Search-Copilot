using JobPilot.Domain;

namespace JobPilot.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_references_only_the_base_class_library()
    {
        var referenced = typeof(DomainAssembly).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name != "netstandard" && !name.StartsWith("System", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(referenced);
    }
}
