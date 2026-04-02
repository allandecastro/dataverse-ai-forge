using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanModifyFieldTool
{
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "displayName", "description", "requiredLevel", "isSearchable", "isAuditEnabled", "maxLength"
    };

    [Description("Generate a plan for modifying an existing field on a Dataverse table. "
        + "Only certain properties can be modified: displayName, description, requiredLevel, isSearchable, isAuditEnabled, maxLength (increase only). "
        + "Returns the PUT payload diff, warnings, and risk level. "
        + "Does NOT modify the field — use execute_modify_field for that.")]
    public Task<FieldModificationPlan> PlanModifyFieldAsync(
        [Description("Logical name of the table (e.g., 'cr123_project')")] string tableLogicalName,
        [Description("Logical name of the field to modify (e.g., 'cr123_description')")] string fieldLogicalName,
        [Description("JSON object of modifications, e.g. {\"displayName\":\"New Name\",\"requiredLevel\":\"ApplicationRequired\",\"maxLength\":500}")] string modificationsJson,
        [Description("Current max length of the field (provide 0 if not a string field)")] int currentMaxLength,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var modifications = new List<FieldModification>();

        var mods = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modificationsJson) ?? [];

        // Validate all requested properties are allowed
        foreach (var key in mods.Keys)
        {
            if (!AllowedProperties.Contains(key))
                warnings.Add($"Property '{key}' cannot be modified. Allowed: {string.Join(", ", AllowedProperties)}.");
        }

        var payload = new Dictionary<string, object>();

        if (mods.TryGetValue("displayName", out var displayNameEl))
        {
            var newValue = displayNameEl.GetString() ?? string.Empty;
            modifications.Add(new FieldModification("DisplayName", "(current)", newValue));
            payload["DisplayName"] = ToolHelpers.BuildLocalizedLabel(newValue);
        }

        if (mods.TryGetValue("description", out var descEl))
        {
            var newValue = descEl.GetString() ?? string.Empty;
            modifications.Add(new FieldModification("Description", "(current)", newValue));
            payload["Description"] = ToolHelpers.BuildLocalizedLabel(newValue);
        }

        if (mods.TryGetValue("requiredLevel", out var reqEl))
        {
            var newValue = reqEl.GetString() ?? "None";
            modifications.Add(new FieldModification("RequiredLevel", "(current)", newValue));
            payload["RequiredLevel"] = new Dictionary<string, object> { ["Value"] = newValue, ["CanBeChanged"] = true };

            if (newValue.Equals("ApplicationRequired", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Setting a field to ApplicationRequired may cause save failures if existing records have null values.");
        }

        if (mods.TryGetValue("isSearchable", out var searchEl))
        {
            var newValue = searchEl.GetBoolean();
            modifications.Add(new FieldModification("IsSearchable", "(current)", newValue.ToString()));
            payload["IsSearchable"] = newValue;
        }

        if (mods.TryGetValue("isAuditEnabled", out var auditEl))
        {
            var newValue = auditEl.GetBoolean();
            modifications.Add(new FieldModification("IsAuditEnabled", "(current)", newValue.ToString()));
            payload["IsAuditEnabled"] = new Dictionary<string, object> { ["Value"] = newValue, ["CanBeChanged"] = true };
        }

        if (mods.TryGetValue("maxLength", out var maxLenEl))
        {
            var newValue = maxLenEl.GetInt32();
            modifications.Add(new FieldModification("MaxLength", currentMaxLength.ToString(), newValue.ToString()));
            payload["MaxLength"] = newValue;

            if (newValue < currentMaxLength && currentMaxLength > 0)
                warnings.Add($"Decreasing MaxLength from {currentMaxLength} to {newValue} may truncate existing data.");
        }

        if (modifications.Count == 0)
            warnings.Add("No valid modifications specified.");

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new FieldModificationPlan(
            TableLogicalName: tableLogicalName,
            FieldLogicalName: fieldLogicalName,
            Modifications: modifications.AsReadOnly(),
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow"));
    }

}
