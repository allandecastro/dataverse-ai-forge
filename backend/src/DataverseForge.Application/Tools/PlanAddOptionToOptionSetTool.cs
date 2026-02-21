using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanAddOptionToOptionSetTool
{
    [Description("Generate a plan for adding a new option value to an existing Dataverse option set (global or local). "
        + "Returns the InsertOptionValue OData JSON payload and validation warnings. "
        + "Does NOT add the option — use execute_add_option_to_option_set for that.")]
    public Task<AddOptionPlan> PlanAddOptionToOptionSetAsync(
        [Description("Name of the option set (global name, e.g., 'cr123_priority')")] string optionSetName,
        [Description("True for global option set, false for table-local option set")] bool isGlobal,
        [Description("Table logical name (required for local option sets, ignored for global)")] string tableLogicalName,
        [Description("Field logical name (required for local option sets, ignored for global)")] string fieldLogicalName,
        [Description("Integer value for the new option (e.g., 100000003)")] int newOptionValue,
        [Description("Display label for the new option (e.g., 'Critical')")] string newOptionLabel,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (!isGlobal && string.IsNullOrWhiteSpace(tableLogicalName))
            warnings.Add("Table logical name is required for local option sets.");

        if (!isGlobal && string.IsNullOrWhiteSpace(fieldLogicalName))
            warnings.Add("Field logical name is required for local option sets.");

        // Build InsertOptionValue payload
        var payload = new Dictionary<string, object>
        {
            ["OptionSetName"] = isGlobal ? optionSetName : string.Empty,
            ["EntityLogicalName"] = isGlobal ? string.Empty : tableLogicalName,
            ["AttributeLogicalName"] = isGlobal ? string.Empty : fieldLogicalName,
            ["Value"] = newOptionValue,
            ["Label"] = BuildLocalizedLabel(newOptionLabel)
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new AddOptionPlan(
            OptionSetName: optionSetName,
            IsGlobal: isGlobal,
            TableLogicalName: tableLogicalName,
            FieldLogicalName: fieldLogicalName,
            NewOptionValue: newOptionValue,
            NewOptionLabel: newOptionLabel,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "green"));
    }

    private static Dictionary<string, object> BuildLocalizedLabel(string label) =>
        new()
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.Label",
            ["LocalizedLabels"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.LocalizedLabel",
                    ["Label"] = label,
                    ["LanguageCode"] = 1033
                }
            }
        };
}
