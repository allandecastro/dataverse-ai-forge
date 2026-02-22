using System.ComponentModel;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreatePluginTool
{
    [Description("Generate a plan for creating a Dataverse plugin. Returns the C# code, registration steps, "
        + "unit test code, and risk level. Does NOT deploy the plugin — use execute_push_to_github for that.")]
    public Task<PluginCreationPlan> PlanCreatePluginAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("The plugin class name (e.g., 'PreValidateWorkOrderCreate')")] string pluginName,
        [Description("The message to register on: 'Create', 'Update', 'Delete', 'Retrieve', 'RetrieveMultiple'")] string message,
        [Description("Pipeline stage: 10 (PreValidation), 20 (PreOperation), 40 (PostOperation)")] int stage,
        [Description("Execution mode: 'Synchronous' or 'Asynchronous'")] string executionMode,
        [Description("Natural language description of the business logic")] string businessLogic,
        [Description("Comma-separated filtering attributes for Update message (empty for other messages)")] string filteringAttributes,
        [Description("Description of the plugin's purpose")] string description,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Validate stage
        if (stage is not (10 or 20 or 40))
            warnings.Add($"Stage {stage} is non-standard. Expected 10 (PreValidation), 20 (PreOperation), or 40 (PostOperation).");

        // Validate message
        var validMessages = new[] { "Create", "Update", "Delete", "Retrieve", "RetrieveMultiple", "Associate", "Disassociate" };
        if (!validMessages.Contains(message, StringComparer.OrdinalIgnoreCase))
            warnings.Add($"Message '{message}' is not a common SDK message.");

        // Validate execution mode
        if (executionMode is not ("Synchronous" or "Asynchronous"))
            warnings.Add($"Execution mode '{executionMode}' is not standard. Expected 'Synchronous' or 'Asynchronous'.");

        // Validate async in pre-stages
        if (executionMode == "Asynchronous" && stage is 10 or 20)
            warnings.Add("Asynchronous plugins cannot run in PreValidation or PreOperation. Use PostOperation instead.");

        // Validate filtering attributes for Update
        if (message.Equals("Update", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(filteringAttributes))
            warnings.Add("Update plugins should specify filtering attributes to avoid unnecessary executions.");

        var filterAttrs = string.IsNullOrWhiteSpace(filteringAttributes)
            ? Array.Empty<string>()
            : filteringAttributes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Generate C# code
        var csharpCode = GeneratePluginCode(pluginName, tableLogicalName, message, stage, businessLogic, filterAttrs, description);

        // Generate unit test code
        var unitTestCode = GenerateUnitTestCode(pluginName, tableLogicalName, message);

        // Generate registration steps
        var registrationSteps = new List<string>
        {
            $"1. Register assembly: {pluginName}.dll",
            $"2. Register step: {pluginName} on {message} of {tableLogicalName}",
            $"   - Stage: {stage} ({StageName(stage)})",
            $"   - Mode: {executionMode}",
        };

        if (filterAttrs.Length > 0)
            registrationSteps.Add($"   - Filtering Attributes: {string.Join(", ", filterAttrs)}");

        var nugetReferences = new List<string>
        {
            "Microsoft.CrmSdk.CoreAssemblies (9.0.2.*)",
            "Microsoft.CrmSdk.Workflow (9.0.2.*)"
        };

        return Task.FromResult(new PluginCreationPlan(
            PluginName: pluginName,
            TableLogicalName: tableLogicalName,
            Message: message,
            Stage: stage,
            ExecutionMode: executionMode,
            Description: description,
            CSharpCode: csharpCode,
            RegistrationSteps: registrationSteps.AsReadOnly(),
            NuGetReferences: nugetReferences.AsReadOnly(),
            UnitTestCode: unitTestCode,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
    }

    private static string StageName(int stage) => stage switch
    {
        10 => "PreValidation",
        20 => "PreOperation",
        40 => "PostOperation",
        _ => $"Stage{stage}"
    };

    private static string GeneratePluginCode(
        string pluginName, string tableLogicalName, string message,
        int stage, string businessLogic, string[] filterAttrs, string description)
    {
        var filterAttrComment = filterAttrs.Length > 0
            ? $"\n    // Filtering Attributes: {string.Join(", ", filterAttrs)}"
            : "";

        return $$"""
            using System;
            using Microsoft.Xrm.Sdk;

            namespace Plugins.{{tableLogicalName.Replace("_", ".")}}
            {
                /// <summary>
                /// {{description}}
                /// Message: {{message}} | Stage: {{stage}} ({{StageName(stage)}}){{filterAttrComment}}
                /// </summary>
                public class {{pluginName}} : IPlugin
                {
                    public void Execute(IServiceProvider serviceProvider)
                    {
                        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                        var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
                        var service = serviceFactory.CreateOrganizationService(context.UserId);

                        tracingService.Trace("{{pluginName}} - Execution started");

                        try
                        {
                            // Validate target entity
                            if (!context.InputParameters.Contains("Target") ||
                                context.InputParameters["Target"] is not Entity target)
                            {
                                tracingService.Trace("Target entity not found in InputParameters");
                                return;
                            }

                            if (target.LogicalName != "{{tableLogicalName}}")
                            {
                                tracingService.Trace("Unexpected entity: {0}", target.LogicalName);
                                return;
                            }

                            // TODO: Implement business logic
                            // Business requirement: {{businessLogic}}
                            tracingService.Trace("Business logic placeholder - implement as needed");

                            tracingService.Trace("{{pluginName}} - Execution completed successfully");
                        }
                        catch (InvalidPluginExecutionException)
                        {
                            throw; // Re-throw business exceptions
                        }
                        catch (Exception ex)
                        {
                            tracingService.Trace("{{pluginName}} - Error: {0}", ex.ToString());
                            throw new InvalidPluginExecutionException(
                                "An error occurred in {{pluginName}}. Please contact your administrator.", ex);
                        }
                    }
                }
            }
            """;
    }

    private static string GenerateUnitTestCode(string pluginName, string tableLogicalName, string message)
    {
        return $$"""
            using System;
            using FakeXrmEasy;
            using Microsoft.Xrm.Sdk;
            using Xunit;

            namespace Tests.{{tableLogicalName.Replace("_", ".")}}
            {
                public class {{pluginName}}Tests
                {
                    private readonly XrmFakedContext _context = new();

                    [Fact]
                    public void {{pluginName}}_ValidTarget_ExecutesSuccessfully()
                    {
                        var target = new Entity("{{tableLogicalName}}")
                        {
                            Id = Guid.NewGuid()
                        };

                        _context.ExecutePluginWith<Plugins.{{tableLogicalName.Replace("_", ".")}}.{{pluginName}}>(
                            new ParameterCollection { { "Target", target } },
                            "{{message}}", {{(message == "Create" ? "null" : "target")}});

                        // Assert expected behavior
                        Assert.True(true); // Replace with actual assertions
                    }

                    [Fact]
                    public void {{pluginName}}_MissingTarget_DoesNotThrow()
                    {
                        _context.ExecutePluginWith<Plugins.{{tableLogicalName.Replace("_", ".")}}.{{pluginName}}>(
                            new ParameterCollection(),
                            "{{message}}", null);
                    }
                }
            }
            """;
    }
}
