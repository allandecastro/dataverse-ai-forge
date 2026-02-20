using System.Reflection;

namespace DataverseForge.Domain.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Domain_ShouldNotReference_ExternalAssemblies()
    {
        var domainAssembly = typeof(Models.ProjectState).Assembly;
        var referencedAssemblies = domainAssembly.GetReferencedAssemblies();

        var externalRefs = referencedAssemblies
            .Where(a => !a.Name!.StartsWith("System", StringComparison.Ordinal)
                     && !a.Name.StartsWith("Microsoft.NETCore", StringComparison.Ordinal)
                     && !a.Name.StartsWith("netstandard", StringComparison.Ordinal))
            .Select(a => a.Name)
            .ToList();

        Assert.Empty(externalRefs);
    }
}
