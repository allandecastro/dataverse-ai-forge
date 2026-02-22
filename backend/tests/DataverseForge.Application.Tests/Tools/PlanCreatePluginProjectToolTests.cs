using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreatePluginProjectToolTests
{
    private readonly PlanCreatePluginProjectTool _tool = new();

    [Fact]
    public async Task PlanCreatePluginProject_ValidInput_ReturnsPlan()
    {
        var plan = await _tool.PlanCreatePluginProjectAsync(
            projectName: "FieldService.Plugins",
            solutionName: "FieldService",
            targetFramework: "net462",
            pluginNames: "PreCreateWorkOrder,PostUpdateTask",
            includeUnitTests: false,
            includeEarlyBound: false,
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("FieldService.Plugins", plan.ProjectName);
        Assert.Equal("FieldService", plan.SolutionName);
        Assert.Equal("net462", plan.TargetFramework);
        Assert.NotEmpty(plan.Files);
        Assert.NotEmpty(plan.CsprojContent);
        Assert.NotEmpty(plan.BuildInstructions);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanCreatePluginProject_NonStandardFramework_Warning()
    {
        var plan = await _tool.PlanCreatePluginProjectAsync(
            projectName: "MyPlugins",
            solutionName: "MySolution",
            targetFramework: "net6.0",
            pluginNames: "OnCreate",
            includeUnitTests: false,
            includeEarlyBound: false,
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("non-standard"));
        Assert.Contains(plan.Warnings, w => w.Contains("net6.0"));
    }

    [Fact]
    public async Task PlanCreatePluginProject_EmptyPlugins_Warning()
    {
        var plan = await _tool.PlanCreatePluginProjectAsync(
            projectName: "EmptyProject",
            solutionName: "MySolution",
            targetFramework: "net462",
            pluginNames: "",
            includeUnitTests: false,
            includeEarlyBound: false,
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("empty"));
    }

    [Fact]
    public async Task PlanCreatePluginProject_GeneratesPluginStubs()
    {
        var plan = await _tool.PlanCreatePluginProjectAsync(
            projectName: "Acme.Plugins",
            solutionName: "AcmeSolution",
            targetFramework: "net462",
            pluginNames: "Foo,Bar",
            includeUnitTests: false,
            includeEarlyBound: false,
            CancellationToken.None);

        Assert.Contains(plan.Files, f => f.Path.Contains("Foo.cs"));
        Assert.Contains(plan.Files, f => f.Path.Contains("Bar.cs"));
        // Also verify scaffolded files
        Assert.Contains(plan.Files, f => f.Path.EndsWith(".csproj"));
        Assert.Contains(plan.Files, f => f.Path.Contains("EntityNames.cs"));
        Assert.Contains(plan.Files, f => f.Path.Contains("FieldNames.cs"));
        Assert.Contains(plan.Files, f => f.Path.Contains("README.md"));
    }

    [Fact]
    public async Task PlanCreatePluginProject_IncludeUnitTests_GeneratesTestProject()
    {
        var plan = await _tool.PlanCreatePluginProjectAsync(
            projectName: "Acme.Plugins",
            solutionName: "AcmeSolution",
            targetFramework: "net462",
            pluginNames: "Foo,Bar",
            includeUnitTests: true,
            includeEarlyBound: false,
            CancellationToken.None);

        Assert.Contains(plan.Files, f => f.Path.Contains("Acme.Plugins.Tests") && f.Path.EndsWith(".csproj"));
        Assert.Contains(plan.Files, f => f.Path.Contains("FooTests.cs"));
        Assert.Contains(plan.Files, f => f.Path.Contains("BarTests.cs"));
    }

    [Fact]
    public async Task PlanCreatePluginProject_IncludeEarlyBound_AddsCoreTools()
    {
        var plan = await _tool.PlanCreatePluginProjectAsync(
            projectName: "Acme.Plugins",
            solutionName: "AcmeSolution",
            targetFramework: "net462",
            pluginNames: "Foo",
            includeUnitTests: false,
            includeEarlyBound: true,
            CancellationToken.None);

        Assert.Contains("Microsoft.CrmSdk.CoreAssemblies", plan.CsprojContent);
        Assert.Contains("Microsoft.CrmSdk.CoreTools", plan.CsprojContent);
    }

    [Fact]
    public async Task PlanCreatePluginProject_DefaultFramework_IsNet462()
    {
        var plan = await _tool.PlanCreatePluginProjectAsync(
            projectName: "DefaultFw.Plugins",
            solutionName: "MySolution",
            targetFramework: "",
            pluginNames: "OnCreate",
            includeUnitTests: false,
            includeEarlyBound: false,
            CancellationToken.None);

        Assert.Equal("net462", plan.TargetFramework);
        // No warning about non-standard framework since it defaulted to net462
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("non-standard"));
    }
}
