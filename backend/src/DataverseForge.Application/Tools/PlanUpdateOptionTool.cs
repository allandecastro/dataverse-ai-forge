using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans a rename or delete on an existing option value (any option set: global,
/// local picklist, or local statuscode). Uses UpdateOptionValue or DeleteOptionValue.
/// </summary>
public class PlanUpdateOptionTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan to RENAME, RECOLOR, or DELETE an existing option value on any option set "
        + "(global, local picklist, multi-select picklist, or statuscode). Uses UpdateOptionValue "
        + "(rename / recolor — both can be combined in one call) or DeleteOptionValue (remove). "
        + "The payload is stored server-side under planId. Pass the planId verbatim to "
        + "execute_update_option after approval. Use this tool — NOT create_field, NOT modify_field — "
        + "to change an option's label, color, or remove it. To LIST existing options before "
        + "updating, call get_field_options first.")]
    public async Task<UpdateOptionPlan> PlanUpdateOptionAsync(
        [Description("Action to perform: 'update' (rename label and/or change color) or 'delete' (remove the option)")] string action,
        [Description("True for global option set, false for table-local")] bool isGlobal,
        [Description("Name of the global option set (used only when isGlobal=true)")] string optionSetName,
        [Description("Table logical name (used only when isGlobal=false)")] string tableLogicalName,
        [Description("Field logical name (used only when isGlobal=false). For status reasons pass 'statuscode'.")] string fieldLogicalName,
        [Description("Integer value of the existing option to rename, recolor, or delete")] int optionValue,
        [Description("New display label (only used when action='update'). Pass empty string to leave the label unchanged when only changing the color, or for action='delete'.")] string newLabel,
        [Description("New hex color for the option chip (e.g., '#FF0000') for action='update'. Pass empty string to leave the color unchanged. Ignored for action='delete'.")] string color,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var actionLower = action.Trim().ToLowerInvariant();

        if (actionLower is not "update" and not "delete")
            warnings.Add($"Action '{action}' is invalid. Expected 'update' or 'delete'.");

        if (!isGlobal && string.IsNullOrWhiteSpace(tableLogicalName))
            warnings.Add("tableLogicalName is required when isGlobal=false.");
        if (!isGlobal && string.IsNullOrWhiteSpace(fieldLogicalName))
            warnings.Add("fieldLogicalName is required when isGlobal=false.");

        var isStatusCode = !isGlobal && fieldLogicalName.Equals("statuscode", StringComparison.OrdinalIgnoreCase);

        // Both UpdateOptionValue and DeleteOptionValue accept the same locator fields.
        var payload = new Dictionary<string, object>
        {
            ["OptionSetName"] = isGlobal ? optionSetName : string.Empty,
            ["EntityLogicalName"] = isGlobal ? string.Empty : tableLogicalName,
            ["AttributeLogicalName"] = isGlobal ? string.Empty : fieldLogicalName,
            ["Value"] = optionValue
        };

        if (actionLower == "update")
        {
            // Both Label and Color are optional individually but at least one
            // must be present for 'update' to do anything meaningful.
            var hasLabel = !string.IsNullOrWhiteSpace(newLabel);
            var hasColor = !string.IsNullOrWhiteSpace(color);
            if (!hasLabel && !hasColor)
                warnings.Add("action='update' needs at least one of newLabel or color to change. Pass either or both.");
            if (hasLabel)
                payload["Label"] = ToolHelpers.BuildLocalizedLabel(newLabel);
            if (hasColor)
            {
                if (!color.StartsWith('#'))
                    warnings.Add($"Color '{color}' should start with '#' (e.g. '#FF0000') — Dataverse may reject it otherwise.");
                payload["Color"] = color;
            }
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"{actionLower}_option_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        var risk = actionLower == "delete" ? PlanRisk.Orange : PlanRisk.Yellow;

        if (actionLower == "delete")
            warnings.Add("Deleting an option value may break records that already use this value.");

        return new UpdateOptionPlan(
            PlanId: planId,
            Action: actionLower,
            OptionSetName: optionSetName,
            IsGlobal: isGlobal,
            IsStatusCode: isStatusCode,
            TableLogicalName: tableLogicalName,
            FieldLogicalName: fieldLogicalName,
            OptionValue: optionValue,
            NewLabel: actionLower == "update" ? newLabel : null,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: risk);
    }
}
