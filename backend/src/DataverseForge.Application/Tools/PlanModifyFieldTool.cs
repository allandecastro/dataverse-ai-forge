using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanModifyFieldTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    // Property catalog: which property keys are valid on which AttributeMetadata
    // derived types. Empty set = "valid on every field type" (the base properties).
    // Names match the JSON keys the agent passes in `modificationsJson`; the value
    // transform happens later per-property.
    private static readonly Dictionary<string, HashSet<string>> AllowedTypesByProperty = new(StringComparer.OrdinalIgnoreCase)
    {
        // Base AttributeMetadata — valid for any field type
        ["displayName"] = new(),
        ["description"] = new(),
        ["requiredLevel"] = new(),
        ["isSearchable"] = new(),
        ["isAuditEnabled"] = new(),
        ["isCustomizable"] = new(),
        ["isValidForAdvancedFind"] = new(),

        // String / Memo
        ["maxLength"] = new(StringComparer.OrdinalIgnoreCase) { "String", "Memo" },
        ["format"] = new(StringComparer.OrdinalIgnoreCase) { "String", "Memo", "Integer", "DateTime", "Lookup" },
        ["imeMode"] = new(StringComparer.OrdinalIgnoreCase) { "String", "Memo", "Decimal", "Double", "Money", "DateTime" },
        ["isLocalizable"] = new(StringComparer.OrdinalIgnoreCase) { "String" },

        // Numeric (Integer, BigInt, Decimal, Double, Money)
        ["minValue"] = new(StringComparer.OrdinalIgnoreCase) { "Integer", "BigInt", "Decimal", "Double", "Money", "DateTime" },
        ["maxValue"] = new(StringComparer.OrdinalIgnoreCase) { "Integer", "BigInt", "Decimal", "Double", "Money", "DateTime" },
        ["precision"] = new(StringComparer.OrdinalIgnoreCase) { "Decimal", "Double", "Money" },

        // DateTime
        ["dateTimeBehavior"] = new(StringComparer.OrdinalIgnoreCase) { "DateTime" },

        // Boolean
        ["defaultValue"] = new(StringComparer.OrdinalIgnoreCase) { "Boolean" },

        // Image
        ["maxHeight"] = new(StringComparer.OrdinalIgnoreCase) { "Image" },
        ["maxWidth"] = new(StringComparer.OrdinalIgnoreCase) { "Image" },
        ["canStoreFullImage"] = new(StringComparer.OrdinalIgnoreCase) { "Image" },

        // File
        ["maxSizeInKB"] = new(StringComparer.OrdinalIgnoreCase) { "File" },
    };

    [Description("Generate a plan for modifying an existing field on a Dataverse table. Properties you can "
        + "change depend on the field type:\n"
        + " • Any field: displayName, description, requiredLevel, isSearchable, isAuditEnabled, "
        + "isCustomizable, isValidForAdvancedFind\n"
        + " • String/Memo: maxLength, format (Email|Text|TextArea|Url|Phone|Json|RichText|TickerSymbol|VersionNumber|PhoneticGuide), imeMode, isLocalizable (String only)\n"
        + " • Integer: minValue, maxValue, format (None|Duration|TimeZone|Language|Locale)\n"
        + " • BigInt: minValue, maxValue\n"
        + " • Decimal/Double/Money: minValue, maxValue, precision, imeMode\n"
        + " • Boolean: defaultValue\n"
        + " • DateTime: format (DateOnly|DateAndTime), dateTimeBehavior (UserLocal|DateOnly|TimeZoneIndependent), minValue, maxValue, imeMode\n"
        + " • Image: maxHeight, maxWidth, canStoreFullImage\n"
        + " • File: maxSizeInKB\n"
        + " • Lookup: format (None|Text)\n"
        + "Properties not allowed for the given fieldType are rejected with a warning. The PUT payload is "
        + "stored server-side under planId. Pass the planId verbatim to execute_modify_field.")]
    public async Task<FieldModificationPlan> PlanModifyFieldAsync(
        [Description("Logical name of the table (e.g., 'cr123_project')")] string tableLogicalName,
        [Description("Logical name of the field to modify (e.g., 'cr123_description')")] string fieldLogicalName,
        [Description("JSON object of modifications, e.g. {\"displayName\":\"New Name\",\"requiredLevel\":\"ApplicationRequired\",\"maxLength\":500} or {\"minValue\":0,\"maxValue\":100,\"precision\":2} for a Decimal field")] string modificationsJson,
        [Description("Current max length of the field (provide 0 if not a string field)")] int currentMaxLength,
        [Description("AttributeType of the existing field (from get_table_details). Required so the PUT payload carries the correct @odata.type discriminator and the right properties pass validation. Common values: 'String', 'Memo', 'Integer', 'Decimal', 'Money', 'Double', 'BigInt', 'Boolean', 'DateTime', 'Picklist', 'MultiSelectPicklist', 'Lookup', 'Customer', 'Owner', 'State', 'Status', 'Uniqueidentifier', 'Image', 'File'.")] string fieldType,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var modifications = new List<FieldModification>();

        var mods = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modificationsJson) ?? [];

        // Validate property names + type compatibility up-front so the agent gets
        // ALL the invalid keys in one shot rather than discovering them one PUT at a time.
        foreach (var key in mods.Keys)
        {
            if (!AllowedTypesByProperty.TryGetValue(key, out var allowedTypes))
            {
                warnings.Add($"Property '{key}' cannot be modified. Allowed: {string.Join(", ", AllowedTypesByProperty.Keys)}.");
                continue;
            }
            if (allowedTypes.Count > 0 && !allowedTypes.Contains(fieldType))
            {
                warnings.Add(
                    $"Property '{key}' cannot be set on a {fieldType} field. "
                    + $"It only applies to: {string.Join(", ", allowedTypes)}.");
            }
        }

        // Dataverse Web API needs @odata.type on metadata PUTs because Attribute
        // is a polymorphic base type. Without it, properties like MaxLength (which
        // only exist on the derived StringAttributeMetadata) get rejected as
        // "invalid property". Recurring bug pattern #4 in CLAUDE.md.
        var odataType = MapFieldTypeToODataType(fieldType);
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = odataType,
            ["LogicalName"] = fieldLogicalName.ToLowerInvariant(),
            ["SchemaName"] = fieldLogicalName,
        };

        // ─── Base properties (apply to any field type) ───
        ApplyString(mods, "displayName", "DisplayName", payload, modifications,
            v => ToolHelpers.BuildLocalizedLabel(v));
        ApplyString(mods, "description", "Description", payload, modifications,
            v => ToolHelpers.BuildLocalizedLabel(v));

        if (mods.TryGetValue("requiredLevel", out var reqEl))
        {
            var v = reqEl.GetString() ?? "None";
            modifications.Add(new FieldModification("RequiredLevel", "(current)", v));
            payload["RequiredLevel"] = new Dictionary<string, object> { ["Value"] = v, ["CanBeChanged"] = true };
            if (v.Equals("ApplicationRequired", StringComparison.OrdinalIgnoreCase))
                warnings.Add("Setting a field to ApplicationRequired may cause save failures if existing records have null values.");
        }

        ApplyBool(mods, "isSearchable", "IsSearchable", payload, modifications);
        ApplyBoolManaged(mods, "isAuditEnabled", "IsAuditEnabled", payload, modifications);
        ApplyBoolManaged(mods, "isCustomizable", "IsCustomizable", payload, modifications);
        ApplyBoolManaged(mods, "isValidForAdvancedFind", "IsValidForAdvancedFind", payload, modifications);

        // ─── String / Memo ───
        if (mods.TryGetValue("maxLength", out var maxLenEl)
            && AllowedTypesByProperty["maxLength"].Contains(fieldType))
        {
            var v = maxLenEl.GetInt32();
            modifications.Add(new FieldModification("MaxLength", currentMaxLength.ToString(), v.ToString()));
            payload["MaxLength"] = v;
            if (v < currentMaxLength && currentMaxLength > 0)
                warnings.Add($"Decreasing MaxLength from {currentMaxLength} to {v} may truncate existing data.");
        }

        if (mods.TryGetValue("format", out var fmtEl)
            && AllowedTypesByProperty["format"].Contains(fieldType))
        {
            var v = fmtEl.GetString() ?? string.Empty;
            modifications.Add(new FieldModification("Format", "(current)", v));
            payload["Format"] = v;
            // Caveat: Format is read-only on String once set in some envs;
            // Dataverse will surface the real error if so.
        }

        ApplyEnumString(mods, "imeMode", "ImeMode", payload, modifications, fieldType,
            allowed: new[] { "Auto", "Inactive", "Active", "Disabled" });

        ApplyBoolManaged(mods, "isLocalizable", "IsLocalizable", payload, modifications,
            allowedFor: fieldType, gateTypes: AllowedTypesByProperty["isLocalizable"]);

        // ─── Numeric (Integer / BigInt / Decimal / Double / Money / DateTime) ───
        ApplyNumeric(mods, "minValue", "MinValue", payload, modifications, fieldType);
        ApplyNumeric(mods, "maxValue", "MaxValue", payload, modifications, fieldType);

        if (mods.TryGetValue("precision", out var precEl)
            && AllowedTypesByProperty["precision"].Contains(fieldType))
        {
            var v = precEl.GetInt32();
            if (v < 0 || v > 4)
                warnings.Add($"Precision {v} is unusual — Dataverse typically allows 0–4 decimals.");
            modifications.Add(new FieldModification("Precision", "(current)", v.ToString()));
            payload["Precision"] = v;
        }

        // ─── DateTime ───
        if (mods.TryGetValue("dateTimeBehavior", out var dtbEl)
            && AllowedTypesByProperty["dateTimeBehavior"].Contains(fieldType))
        {
            var v = dtbEl.GetString() ?? "UserLocal";
            string[] allowed = ["UserLocal", "DateOnly", "TimeZoneIndependent"];
            if (!allowed.Contains(v, StringComparer.OrdinalIgnoreCase))
                warnings.Add($"DateTimeBehavior '{v}' is not recognized. Allowed: {string.Join(", ", allowed)}.");
            modifications.Add(new FieldModification("DateTimeBehavior", "(current)", v));
            // DateTimeBehavior is a complex type with a Value property.
            payload["DateTimeBehavior"] = new Dictionary<string, object> { ["Value"] = v };
            warnings.Add("Changing DateTimeBehavior is irreversible and affects how stored values are interpreted across time zones.");
        }

        // ─── Boolean ───
        if (mods.TryGetValue("defaultValue", out var defEl)
            && AllowedTypesByProperty["defaultValue"].Contains(fieldType))
        {
            var v = defEl.GetBoolean();
            modifications.Add(new FieldModification("DefaultValue", "(current)", v.ToString()));
            payload["DefaultValue"] = v;
        }

        // ─── Image ───
        ApplyInt(mods, "maxHeight", "MaxHeight", payload, modifications, fieldType);
        ApplyInt(mods, "maxWidth", "MaxWidth", payload, modifications, fieldType);
        ApplyBool(mods, "canStoreFullImage", "CanStoreFullImage", payload, modifications,
            allowedFor: fieldType, gateTypes: AllowedTypesByProperty["canStoreFullImage"]);

        // ─── File ───
        ApplyInt(mods, "maxSizeInKB", "MaxSizeInKB", payload, modifications, fieldType);

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

    // ─── helpers ───────────────────────────────────────────

    private static void ApplyString(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> mods2,
        Func<string, object> transform)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        var v = el.GetString() ?? string.Empty;
        mods2.Add(new FieldModification(odataKey, "(current)", v));
        payload[odataKey] = transform(v);
    }

    private static void ApplyBool(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> mods2,
        string? allowedFor = null,
        HashSet<string>? gateTypes = null)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (gateTypes is not null && allowedFor is not null && !gateTypes.Contains(allowedFor)) return;
        var v = el.GetBoolean();
        mods2.Add(new FieldModification(odataKey, "(current)", v.ToString()));
        payload[odataKey] = v;
    }

    private static void ApplyBoolManaged(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> mods2,
        string? allowedFor = null,
        HashSet<string>? gateTypes = null)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (gateTypes is not null && allowedFor is not null && !gateTypes.Contains(allowedFor)) return;
        var v = el.GetBoolean();
        mods2.Add(new FieldModification(odataKey, "(current)", v.ToString()));
        payload[odataKey] = new Dictionary<string, object> { ["Value"] = v, ["CanBeChanged"] = true };
    }

    private static void ApplyEnumString(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> mods2,
        string fieldType,
        string[] allowed)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (!AllowedTypesByProperty[key].Contains(fieldType)) return;
        var v = el.GetString() ?? string.Empty;
        if (!allowed.Contains(v, StringComparer.OrdinalIgnoreCase))
        {
            // Caller's warnings list is not visible here; let Dataverse reject if invalid.
        }
        mods2.Add(new FieldModification(odataKey, "(current)", v));
        payload[odataKey] = v;
    }

    private static void ApplyInt(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> mods2,
        string fieldType)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (!AllowedTypesByProperty[key].Contains(fieldType)) return;
        var v = el.GetInt32();
        mods2.Add(new FieldModification(odataKey, "(current)", v.ToString()));
        payload[odataKey] = v;
    }

    /// <summary>
    /// MinValue / MaxValue parse depending on fieldType: BigInt → Int64, Integer →
    /// Int32, Decimal/Double/Money → double (JSON has no decimal). DateTime → string.
    /// </summary>
    private static void ApplyNumeric(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> mods2,
        string fieldType)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (!AllowedTypesByProperty[key].Contains(fieldType)) return;

        object value;
        string display;
        switch (fieldType)
        {
            case "Integer":
                value = el.GetInt32();
                display = value.ToString()!;
                break;
            case "BigInt":
                value = el.GetInt64();
                display = value.ToString()!;
                break;
            case "Decimal":
            case "Double":
            case "Money":
                value = el.GetDouble();
                display = value.ToString()!;
                break;
            case "DateTime":
                value = el.GetString() ?? string.Empty;
                display = (string)value;
                break;
            default:
                return;
        }

        mods2.Add(new FieldModification(odataKey, "(current)", display));
        payload[odataKey] = value;
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
