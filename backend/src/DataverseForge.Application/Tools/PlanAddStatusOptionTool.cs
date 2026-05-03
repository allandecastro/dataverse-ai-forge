using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans a new status-reason option on a table's `statuscode` field.
/// Dataverse rejects InsertOptionValue for `statuscode` because each status reason
/// must be mapped to an existing State value (0 = Active, 1 = Inactive). This tool
/// uses the InsertStatusValue action which accepts that mapping.
/// </summary>
public class PlanAddStatusOptionTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Generate a plan for adding a new status-reason option to a table's `statuscode` "
        + "system field. Dataverse status reasons MUST be mapped to a State value (0 = Active, "
        + "1 = Inactive). The InsertStatusValue payload is stored server-side under planId. "
        + "Pass the planId verbatim to execute_add_status_option after approval. "
        + "Use this tool — NOT plan_add_option_to_option_set, NOT plan_create_field — to add "
        + "a status reason like 'Maintenance', 'Assigned', 'Retired' to a table.")]
    public async Task<AddStatusOptionPlan> PlanAddStatusOptionAsync(
        [Description("Logical name of the table whose `statuscode` field will get a new option (e.g., 'adc_vehicle')")] string tableLogicalName,
        [Description("State value to map this status to: 0 for Active, 1 for Inactive")] int stateValue,
        [Description("Integer value for the new status reason (e.g., 100000003)")] int newStatusValue,
        [Description("Display label for the new status reason (e.g., 'Maintenance')")] string newStatusLabel,
        [Description("Optional hex color for the status-reason chip in the Dataverse UI (e.g., '#0070FF'). Pass an empty string for no color.")] string color,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (stateValue is not 0 and not 1)
            warnings.Add($"State value {stateValue} is unusual — Dataverse defines 0 = Active, 1 = Inactive.");

        // Best-effort: query existing status reasons and surface conflicts.
        try
        {
            var existing = await dataverseClient.GetLocalOptionSetOptionsAsync(tableLogicalName, "statuscode", cancellationToken);
            var conflicts = new List<string>();
            foreach (var opt in existing)
            {
                if (opt.Value == newStatusValue)
                    conflicts.Add($"EXACT value match: status {opt.Value} = '{opt.Label}' already exists — creating again will fail.");
                else if (ToolHelpers.LooksLikeSameName(opt.Label, newStatusLabel))
                {
                    var stateLbl = opt.StateValue == 0 ? "Active" : opt.StateValue == 1 ? "Inactive" : $"State={opt.StateValue}";
                    conflicts.Add($"Similar status reason exists: {opt.Value} = '{opt.Label}' (mapped to {stateLbl}).");
                }
            }
            if (conflicts.Count > 0)
            {
                warnings.Add("Possible duplicate status reason — confirm with the user before adding:");
                warnings.AddRange(conflicts);
            }
        }
        catch (Exception) { /* non-fatal */ }

        // InsertStatusValue payload: bound to a table's statuscode field.
        // https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/insertstatusvalue
        // The state mapping parameter is "StateCode" (NOT "State"). Past failure:
        // sending "State" returned HTTP 400 "the 'State' parameter ... is invalid for
        // the InsertStatusValue operation".
        var payload = new Dictionary<string, object>
        {
            ["StateCode"] = stateValue,
            ["Value"] = newStatusValue,
            ["EntityLogicalName"] = tableLogicalName,
            ["AttributeLogicalName"] = "statuscode",
            ["Label"] = ToolHelpers.BuildLocalizedLabel(newStatusLabel)
        };

        if (!string.IsNullOrWhiteSpace(color))
        {
            // InsertStatusValue accepts an optional Color string (hex like "#0070FF").
            // Tag the chip in the Dataverse UI with the color.
            payload["Color"] = color;

            if (!color.StartsWith('#'))
                warnings.Add($"Color '{color}' should start with '#' (e.g. '#0070FF') — Dataverse may reject it otherwise.");
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"add_status_option_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new AddStatusOptionPlan(
            PlanId: planId,
            TableLogicalName: tableLogicalName,
            StateValue: stateValue,
            NewStatusValue: newStatusValue.ToString(),
            NewStatusLabel: newStatusLabel,
            Color: color ?? string.Empty,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow");
    }
}
