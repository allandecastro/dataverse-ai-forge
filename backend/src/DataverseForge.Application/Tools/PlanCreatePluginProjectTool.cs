using System.ComponentModel;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreatePluginProjectTool
{
    [Description("Generate a plan for a complete plugin project structure including .csproj, "
        + "folder structure, constants, and build instructions. Does NOT create files — use execute_push_to_github for that.")]
    public Task<PluginProjectPlan> PlanCreatePluginProjectAsync(
        [Description("The project name (e.g., 'FieldService.Plugins')")] string projectName,
        [Description("The Dataverse solution unique name")] string solutionName,
        [Description("Target framework (default: 'net462')")] string targetFramework,
        [Description("Comma-separated list of plugin class names to include")] string pluginNames,
        [Description("Whether to include unit test project")] bool includeUnitTests,
        [Description("Whether to include early-bound entity classes")] bool includeEarlyBound,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(targetFramework))
            targetFramework = "net462";

        if (targetFramework != "net462")
            warnings.Add($"Target framework '{targetFramework}' is non-standard for Dataverse plugins. Recommended: net462.");

        var plugins = string.IsNullOrWhiteSpace(pluginNames)
            ? Array.Empty<string>()
            : pluginNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (plugins.Length == 0)
            warnings.Add("No plugin names specified. The project will be empty.");

        var files = new List<PluginProjectFile>();

        // .csproj
        var csprojContent = GenerateCsproj(projectName, targetFramework, includeEarlyBound);
        files.Add(new PluginProjectFile($"{projectName}/{projectName}.csproj", csprojContent));

        // Constants
        files.Add(new PluginProjectFile($"{projectName}/Constants/EntityNames.cs", GenerateEntityNamesClass(projectName)));
        files.Add(new PluginProjectFile($"{projectName}/Constants/FieldNames.cs", GenerateFieldNamesClass(projectName)));

        // Plugin stubs
        foreach (var plugin in plugins)
        {
            files.Add(new PluginProjectFile($"{projectName}/Plugins/{plugin}.cs", GeneratePluginStub(projectName, plugin)));
        }

        // Unit test project
        if (includeUnitTests)
        {
            var testCsproj = GenerateTestCsproj(projectName, targetFramework);
            files.Add(new PluginProjectFile($"{projectName}.Tests/{projectName}.Tests.csproj", testCsproj));

            foreach (var plugin in plugins)
            {
                files.Add(new PluginProjectFile($"{projectName}.Tests/{plugin}Tests.cs", GenerateTestStub(projectName, plugin)));
            }
        }

        // README
        files.Add(new PluginProjectFile($"{projectName}/README.md", GenerateReadme(projectName, solutionName, plugins)));

        var buildInstructions = $"""
            Build Instructions:
            1. cd {projectName}
            2. dotnet restore
            3. dotnet build --configuration Release
            4. Sign the assembly with a strong name key
            5. Register using Plugin Registration Tool or spkl
            """;

        return Task.FromResult(new PluginProjectPlan(
            ProjectName: projectName,
            SolutionName: solutionName,
            TargetFramework: targetFramework,
            Files: files.AsReadOnly(),
            CsprojContent: csprojContent,
            BuildInstructions: buildInstructions,
            Warnings: warnings.AsReadOnly()));
    }

    private static string GenerateCsproj(string projectName, string targetFramework, bool includeEarlyBound)
    {
        var earlyBoundRef = includeEarlyBound
            ? "\n    <PackageReference Include=\"Microsoft.CrmSdk.CoreTools\" Version=\"9.1.*\" />"
            : "";

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{targetFramework}</TargetFramework>
                <AssemblyName>{projectName}</AssemblyName>
                <RootNamespace>{projectName}</RootNamespace>
                <SignAssembly>true</SignAssembly>
                <AssemblyOriginatorKeyFile>Key.snk</AssemblyOriginatorKeyFile>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.*" />
                <PackageReference Include="Microsoft.CrmSdk.Workflow" Version="9.0.2.*" />{earlyBoundRef}
              </ItemGroup>
            </Project>
            """;
    }

    private static string GenerateTestCsproj(string projectName, string targetFramework)
    {
        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>{targetFramework}</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.6.*" />
                <PackageReference Include="xunit.runner.visualstudio" Version="2.5.*" />
                <PackageReference Include="FakeXrmEasy" Version="2.4.*" />
                <ProjectReference Include="../{projectName}/{projectName}.csproj" />
              </ItemGroup>
            </Project>
            """;
    }

    private static string GenerateEntityNamesClass(string projectName) =>
        $"namespace {projectName}.Constants\n" +
        "{\n" +
        "    public static class EntityNames\n" +
        "    {\n" +
        "        // Add entity logical names here\n" +
        "    }\n" +
        "}\n";

    private static string GenerateFieldNamesClass(string projectName) =>
        $"namespace {projectName}.Constants\n" +
        "{\n" +
        "    public static class FieldNames\n" +
        "    {\n" +
        "        // Add field logical names here\n" +
        "    }\n" +
        "}\n";

    private static string GeneratePluginStub(string projectName, string pluginName) =>
        "using System;\n" +
        "using Microsoft.Xrm.Sdk;\n\n" +
        $"namespace {projectName}.Plugins\n" +
        "{\n" +
        $"    public class {pluginName} : IPlugin\n" +
        "    {\n" +
        "        public void Execute(IServiceProvider serviceProvider)\n" +
        "        {\n" +
        "            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));\n" +
        "            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));\n" +
        "            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));\n" +
        "            var service = serviceFactory.CreateOrganizationService(context.UserId);\n\n" +
        $"            tracingService.Trace(\"{pluginName} started\");\n\n" +
        "            try\n" +
        "            {\n" +
        "                // TODO: Implement business logic\n" +
        $"                tracingService.Trace(\"{pluginName} completed\");\n" +
        "            }\n" +
        "            catch (InvalidPluginExecutionException)\n" +
        "            {\n" +
        "                throw;\n" +
        "            }\n" +
        "            catch (Exception ex)\n" +
        "            {\n" +
        $"                tracingService.Trace(\"{pluginName} error: {{0}}\", ex.ToString());\n" +
        "                throw new InvalidPluginExecutionException(\"An error occurred.\", ex);\n" +
        "            }\n" +
        "        }\n" +
        "    }\n" +
        "}\n";

    private static string GenerateTestStub(string projectName, string pluginName) =>
        "using FakeXrmEasy;\n" +
        "using Microsoft.Xrm.Sdk;\n" +
        "using Xunit;\n\n" +
        $"namespace {projectName}.Tests\n" +
        "{\n" +
        $"    public class {pluginName}Tests\n" +
        "    {\n" +
        "        private readonly XrmFakedContext _context = new();\n\n" +
        "        [Fact]\n" +
        $"        public void {pluginName}_ExecutesSuccessfully()\n" +
        "        {\n" +
        "            var target = new Entity(\"entity_name\") { Id = System.Guid.NewGuid() };\n\n" +
        $"            _context.ExecutePluginWith<{projectName}.Plugins.{pluginName}>(\n" +
        "                new ParameterCollection { { \"Target\", target } },\n" +
        "                \"Create\", null);\n" +
        "        }\n" +
        "    }\n" +
        "}\n";

    private static string GenerateReadme(string projectName, string solutionName, string[] plugins)
    {
        var pluginList = plugins.Length > 0
            ? string.Join("\n", plugins.Select(p => $"- {p}"))
            : "- (none defined)";

        return $"""
            # {projectName}

            Dataverse plugin project for solution **{solutionName}**.

            ## Plugins

            {pluginList}

            ## Build

            ```bash
            dotnet restore
            dotnet build --configuration Release
            ```

            ## Deploy

            Use the Plugin Registration Tool to register the assembly and steps.
            """;
    }
}
