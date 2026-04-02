using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateGlobalOptionSetTool
{
    [Description("Generate a plan for creating a global option set in Dataverse. "
        + "Returns the full OData JSON payload and validation warnings. "
        + "Does NOT create the option set — use execute_create_global_option_set for that.")]
    public Task<GlobalOptionSetCreationPlan> PlanCreateGlobalOptionSetAsync(
        [Description("Schema name for the global option set (e.g., 'cr123_priority')")] string name,
        [Description("Display name for the global option set (e.g., 'Priority')")] string displayName,
        [Description("Description of the option set")] string description,
        [Description("Type of option set: 'Picklist' or 'Boolean'")] string optionSetType,
        [Description("JSON array of options, each with 'value' (int) and 'label' (string), e.g. [{\"value\":100000,\"label\":\"Low\"}]")] string optionsJson,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var options = JsonSerializer.Deserialize<List<OptionInput>>(optionsJson) ?? [];

        if (options.Count == 0)
            warnings.Add("Option set has no options defined. At least one option is recommended.");

        if (options.Count > 200)
            warnings.Add("Option sets with more than 200 options may impact form load performance.");

        if (!name.StartsWith(publisherPrefix + "_"))
            warnings.Add($"Option set name '{name}' should start with publisher prefix '{publisherPrefix}_'.");

        var optionEntries = options.Select(o => new OptionValueEntry(o.Value, o.Label)).ToList();

        // Build OData JSON payload
        var optionsList = options.Select(o => new Dictionary<string, object>
        {
            ["Value"] = o.Value,
            ["Label"] = ToolHelpers.BuildLocalizedLabel(o.Label)
        }).ToArray();

        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadata",
            ["Name"] = name,
            ["DisplayName"] = ToolHelpers.BuildLocalizedLabel(displayName),
            ["Description"] = ToolHelpers.BuildLocalizedLabel(description),
            ["IsGlobal"] = true,
            ["OptionSetType"] = optionSetType,
            ["Options"] = optionsList
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new GlobalOptionSetCreationPlan(
            Name: name,
            DisplayName: displayName,
            Description: description,
            OptionSetType: optionSetType,
            Options: optionEntries.AsReadOnly(),
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow"));
    }

    private sealed class OptionInput
    {
        public int Value { get; set; }
        public string Label { get; set; } = string.Empty;
    }
}
