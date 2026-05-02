using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanAddOptionToOptionSetTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Generate a plan for adding a new option value to an existing Dataverse option set "
        + "(global or local picklist). Stores the InsertOptionValue payload server-side under planId. "
        + "Pass the planId verbatim to execute_add_option_to_option_set after approval. "
        + "DO NOT use this for `statuscode` (status reason) fields — use plan_add_status_option instead, "
        + "which handles the required state mapping (Active/Inactive).")]
    public async Task<AddOptionPlan> PlanAddOptionToOptionSetAsync(
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

        if (!isGlobal && fieldLogicalName.Equals("statuscode", StringComparison.OrdinalIgnoreCase))
            warnings.Add("statuscode is a status-reason field — InsertOptionValue does not work. Use plan_add_status_option instead.");

        // Best-effort: read existing options and surface conflicts.
        try
        {
            var existing = isGlobal
                ? await dataverseClient.GetGlobalOptionSetOptionsAsync(optionSetName, cancellationToken)
                : await dataverseClient.GetLocalOptionSetOptionsAsync(tableLogicalName, fieldLogicalName, cancellationToken);

            var conflicts = new List<string>();
            foreach (var opt in existing)
            {
                if (opt.Value == newOptionValue)
                    conflicts.Add($"EXACT value match: option {opt.Value} = '{opt.Label}' already exists — creating again will fail.");
                else if (ToolHelpers.LooksLikeSameName(opt.Label, newOptionLabel))
                    conflicts.Add($"Similar option already exists: {opt.Value} = '{opt.Label}'.");
            }
            if (conflicts.Count > 0)
            {
                warnings.Add("Possible duplicate option — confirm with the user before adding:");
                warnings.AddRange(conflicts);
            }
        }
        catch (Exception) { /* non-fatal */ }

        var payload = new Dictionary<string, object>
        {
            ["OptionSetName"] = isGlobal ? optionSetName : string.Empty,
            ["EntityLogicalName"] = isGlobal ? string.Empty : tableLogicalName,
            ["AttributeLogicalName"] = isGlobal ? string.Empty : fieldLogicalName,
            ["Value"] = newOptionValue,
            ["Label"] = ToolHelpers.BuildLocalizedLabel(newOptionLabel)
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"add_option_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new AddOptionPlan(
            PlanId: planId,
            OptionSetName: optionSetName,
            IsGlobal: isGlobal,
            TableLogicalName: tableLogicalName,
            FieldLogicalName: fieldLogicalName,
            NewOptionValue: newOptionValue,
            NewOptionLabel: newOptionLabel,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "green");
    }
}
