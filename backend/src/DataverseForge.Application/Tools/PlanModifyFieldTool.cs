using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanModifyFieldTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "displayName", "description", "requiredLevel", "isSearchable", "isAuditEnabled", "maxLength"
    };

    [Description("Generate a plan for modifying an existing field on a Dataverse table. "
        + "Only certain properties can be modified: displayName, description, requiredLevel, "
        + "isSearchable, isAuditEnabled, maxLength (increase only). The PUT payload is stored "
        + "server-side under planId so it cannot be mangled. Pass the planId verbatim to "
        + "execute_modify_field after approval. Use this tool to UPDATE an existing field — "
        + "do NOT call create_field for that.")]
    public async Task<FieldModificationPlan> PlanModifyFieldAsync(
        [Description("Logical name of the table (e.g., 'cr123_project')")] string tableLogicalName,
        [Description("Logical name of the field to modify (e.g., 'cr123_description')")] string fieldLogicalName,
        [Description("JSON object of modifications, e.g. {\"displayName\":\"New Name\",\"requiredLevel\":\"ApplicationRequired\",\"maxLength\":500}")] string modificationsJson,
        [Description("Current max length of the field (provide 0 if not a string field)")] int currentMaxLength,
        [Description("AttributeType of the existing field (from get_table_details). Required so the PUT payload carries the correct @odata.type discriminator. Common values: 'String', 'Memo', 'Integer', 'Decimal', 'Money', 'Double', 'BigInt', 'Boolean', 'DateTime', 'Picklist', 'MultiSelectPicklist', 'Lookup', 'Customer', 'Owner', 'State', 'Status', 'Uniqueidentifier', 'Image', 'File'.")] string fieldType,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var modifications = new List<FieldModification>();

        var mods = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modificationsJson) ?? [];

        foreach (var key in mods.Keys)
        {
            if (!AllowedProperties.Contains(key))
                warnings.Add($"Property '{key}' cannot be modified. Allowed: {string.Join(", ", AllowedProperties)}.");
        }

        // Dataverse Web API needs @odata.type on metadata PUTs because Attribute
        // is a polymorphic base type. Without it, properties like MaxLength (which
        // only exist on the derived StringAttributeMetadata) get rejected as
        // "invalid property". Recurring bug pattern #4 in CLAUDE.md.
        var odataType = MapFieldTypeToODataType(fieldType);
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = odataType,
            // SchemaName is required on metadata PUT — re-send the existing logical
            // name capitalized as a passable schema name. Dataverse accepts the
            // logical name directly here for in-place updates.
            ["LogicalName"] = fieldLogicalName.ToLowerInvariant(),
            ["SchemaName"] = fieldLogicalName,
        };

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

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"modify_field_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new FieldModificationPlan(
            PlanId: planId,
            TableLogicalName: tableLogicalName,
            FieldLogicalName: fieldLogicalName,
            Modifications: modifications.AsReadOnly(),
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow");
    }

    /// <summary>
    /// Maps the AttributeType returned by Dataverse to the OData type discriminator
    /// expected on metadata PUT bodies. Without this, properties like MaxLength get
    /// rejected because they live on derived types (StringAttributeMetadata) rather
    /// than the polymorphic AttributeMetadata base.
    /// </summary>
    private static string MapFieldTypeToODataType(string attributeType) => attributeType switch
    {
        "String" => "Microsoft.Dynamics.CRM.StringAttributeMetadata",
        "Memo" => "Microsoft.Dynamics.CRM.MemoAttributeMetadata",
        "Integer" => "Microsoft.Dynamics.CRM.IntegerAttributeMetadata",
        "BigInt" => "Microsoft.Dynamics.CRM.BigIntAttributeMetadata",
        "Decimal" => "Microsoft.Dynamics.CRM.DecimalAttributeMetadata",
        "Double" => "Microsoft.Dynamics.CRM.DoubleAttributeMetadata",
        "Money" => "Microsoft.Dynamics.CRM.MoneyAttributeMetadata",
        "Boolean" => "Microsoft.Dynamics.CRM.BooleanAttributeMetadata",
        "DateTime" => "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata",
        "Picklist" => "Microsoft.Dynamics.CRM.PicklistAttributeMetadata",
        "MultiSelectPicklist" => "Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata",
        "State" => "Microsoft.Dynamics.CRM.StateAttributeMetadata",
        "Status" => "Microsoft.Dynamics.CRM.StatusAttributeMetadata",
        "Lookup" => "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
        "Customer" => "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
        "Owner" => "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
        "Uniqueidentifier" => "Microsoft.Dynamics.CRM.UniqueIdentifierAttributeMetadata",
        "Image" => "Microsoft.Dynamics.CRM.ImageAttributeMetadata",
        "File" => "Microsoft.Dynamics.CRM.FileAttributeMetadata",
        _ => "Microsoft.Dynamics.CRM.StringAttributeMetadata", // safe fallback
    };
}
