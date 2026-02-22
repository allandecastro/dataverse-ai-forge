using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateBpfTool
{
    [Description("Generate a plan for creating a Business Process Flow (BPF) in Dataverse. Returns the stage flow diagram, "
        + "OData JSON payload, warnings, and risk level. Does NOT create the BPF — use execute_create_bpf for that.")]
    public Task<BpfCreationPlan> PlanCreateBpfAsync(
        [Description("The unique name for the BPF (e.g., 'cr123_WorkOrderProcess')")] string bpfName,
        [Description("The display name (e.g., 'Work Order Process')")] string displayName,
        [Description("Description of the BPF")] string description,
        [Description("The primary table logical name")] string primaryTable,
        [Description("Pipe-separated stage definitions. Each stage: 'name|displayName|field1:req,field2:opt,field3:req'. Use 'req' for required, 'opt' for optional.")] string stagesDefinition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var stages = ParseStages(stagesDefinition);

        if (stages.Count == 0)
            warnings.Add("No stages defined. BPF requires at least one stage.");

        if (stages.Count > 30)
            warnings.Add($"BPF has {stages.Count} stages. Dataverse supports max 30 stages.");

        foreach (var stage in stages)
        {
            if (stage.Steps.Count > 5)
                warnings.Add($"Stage '{stage.DisplayName}' has {stage.Steps.Count} steps. Recommended: 2-5 per stage.");

            if (stage.Steps.Count == 0)
                warnings.Add($"Stage '{stage.DisplayName}' has no steps defined.");
        }

        // Build stage flow diagram
        var diagram = BuildStageFlowDiagram(stages);

        // Build OData payload (workflows with category=4 for BPF)
        var payload = new Dictionary<string, object>
        {
            ["name"] = bpfName,
            ["uniquename"] = bpfName,
            ["category"] = 4, // BPF
            ["primaryentity"] = primaryTable,
            ["description"] = description,
            ["type"] = 1 // Definition
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new BpfCreationPlan(
            BpfName: bpfName,
            DisplayName: displayName,
            Description: description,
            PrimaryTable: primaryTable,
            Stages: stages.AsReadOnly(),
            StageFlowDiagram: diagram,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
    }

    private static List<BpfStage> ParseStages(string stagesDefinition)
    {
        if (string.IsNullOrWhiteSpace(stagesDefinition))
            return [];

        var stages = new List<BpfStage>();
        var stageEntries = stagesDefinition.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Stages are in groups of 3: name, displayName, steps
        for (var i = 0; i + 2 < stageEntries.Length; i += 3)
        {
            var name = stageEntries[i];
            var displayNameValue = stageEntries[i + 1];
            var stepsDef = stageEntries[i + 2];

            var steps = new List<BpfStageStep>();
            var seq = 1;

            foreach (var stepDef in stepsDef.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = stepDef.Split(':', 2);
                var fieldName = parts[0].Trim();
                var isRequired = parts.Length > 1 && parts[1].Trim().Equals("req", StringComparison.OrdinalIgnoreCase);
                steps.Add(new BpfStageStep(fieldName, isRequired, seq++));
            }

            stages.Add(new BpfStage(name, displayNameValue, "Qualify", null, steps.AsReadOnly()));
        }

        return stages;
    }

    private static string BuildStageFlowDiagram(List<BpfStage> stages)
    {
        if (stages.Count == 0)
            return "(empty BPF)";

        var parts = stages.Select(s =>
        {
            var stepList = string.Join(", ", s.Steps.Select(st => st.IsRequired ? $"*{st.FieldLogicalName}" : st.FieldLogicalName));
            return $"[{s.DisplayName}: {stepList}]";
        });

        return string.Join(" → ", parts);
    }
}
