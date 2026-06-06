using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateFieldTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Generate a plan for creating a new field (column) on a Dataverse table. Returns a planId, "
        + "validation warnings, and risk level. The full OData JSON payload is stored server-side under "
        + "planId so it cannot be mangled. Pass the planId verbatim to execute_create_field after approval.")]
    public async Task<FieldCreationPlan> PlanCreateFieldAsync(
        [Description("Logical name of the target table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("Display name for the field (e.g., 'Priority')")] string displayName,
        [Description("Description of the field")] string description,
        [Description("Field type: string, memo, integer, decimal, float, money, boolean, dateTime, "
            + "localOptionSet, localMultiSelectOptionSet, globalOptionSet, file, image, autoNumber, "
            + "calculated, rollup. NOTE: 'lookup' and 'customer' are NOT valid here — call "
            + "plan_create_one_to_many_relationship to create lookup columns (Dataverse only "
            + "allows lookup creation via the relationship action).")] string fieldType,
        [Description("JSON string with type-specific configuration. Examples: "
            + "string → {\"maxLength\":200,\"format\":\"Email\"}; "
            + "localOptionSet → {\"options\":[{\"label\":\"Low\",\"value\":100000000,\"color\":\"#2CA02C\"},{\"label\":\"High\",\"value\":100000001,\"color\":\"#D62728\"}]} (color is OPTIONAL hex #RRGGBB); "
            + "globalOptionSet → {\"globalOptionSetName\":\"adi_priority\"}; "
            + "calculated → {\"returnType\":\"money\",\"precision\":2} (returnType: string|integer|bigint|decimal|float|money|dateTime|boolean — defaults to string); "
            + "rollup → {\"returnType\":\"integer\"} (default integer for COUNT, use money for SUM on Money fields); "
            + "autoNumber → {\"format\":\"GIFT-{SEQNUM:5}\",\"maxLength\":100}")] string fieldTypeConfigJson,
        [Description("Required level: 'None', 'ApplicationRequired', or 'Recommended'")] string requiredLevel,
        [Description("Whether the field is searchable")] bool isSearchable,
        [Description("Whether auditing is enabled for this field")] bool isAuditEnabled,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var config = string.IsNullOrWhiteSpace(fieldTypeConfigJson)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(fieldTypeConfigJson) ?? [];

        // Derive logical and schema names — sanitize to drop parens, hyphens, accents, etc.
        // that Dataverse rejects ([A-Za-z0-9_] only, must start with a letter).
        var sanitized = ToolHelpers.SanitizeSchemaName(displayName);
        if (string.IsNullOrEmpty(sanitized))
        {
            warnings.Add($"Display name '{displayName}' produced an empty schema name after sanitization.");
            sanitized = "Field";
        }
        var fieldLogicalName = $"{publisherPrefix}_{sanitized.ToLowerInvariant()}";
        var fieldSchemaName = $"{publisherPrefix}_{sanitized}";

        if (!string.Equals(sanitized, displayName.Replace(" ", ""), StringComparison.Ordinal))
        {
            warnings.Add($"Display name contained characters Dataverse rejects in schema names; logical name will be '{fieldLogicalName}'.");
        }

        // Best-effort: query the table's existing fields and surface similar matches so
        // the user can decide whether they're recreating something that already exists.
        // An EXACT match is a hard-stop — Dataverse will reject the second create, so we
        // refuse to plan it. Past regression: agent re-included an already-created field
        // in a retry batch because the warning was easy to miss; the second batch then
        // partially failed and the model spun on it. Returning a `rejected_field_exists_*`
        // plan with a clear instruction makes the skip path unambiguous.
        try
        {
            var details = await dataverseClient.GetTableDetailsAsync(tableLogicalName, cancellationToken);
            var conflicts = new List<string>();
            string? exactExistingDisplay = null;
            string? exactExistingLogical = null;
            string? exactExistingType = null;
            foreach (var col in details.Columns)
            {
                var exact = string.Equals(col.LogicalName, fieldLogicalName, StringComparison.OrdinalIgnoreCase);
                var similarLogical = !exact && ToolHelpers.LooksLikeSameName(col.LogicalName, fieldLogicalName);
                var similarDisplay = ToolHelpers.LooksLikeSameName(col.DisplayName, displayName);
                if (exact)
                {
                    exactExistingDisplay = col.DisplayName;
                    exactExistingLogical = col.LogicalName;
                    exactExistingType = col.AttributeType;
                    conflicts.Add($"EXACT match: '{col.DisplayName}' ({col.LogicalName}, {col.AttributeType}) — creating again will fail.");
                }
                else if (similarLogical || similarDisplay)
                {
                    conflicts.Add($"Similar field already exists: '{col.DisplayName}' ({col.LogicalName}, {col.AttributeType}).");
                }
            }

            if (exactExistingLogical is not null)
            {
                // Reject the plan outright. No payload is persisted; ExecuteCreateField
                // will refuse the planId. The agent should NOT include this in any batch.
                var rejection = new List<string>
                {
                    $"REJECTED: Field '{exactExistingLogical}' already exists on table "
                    + $"'{tableLogicalName}' (display: '{exactExistingDisplay}', type: '{exactExistingType}'). "
                    + "DO NOT include this in any request_batch_approval call. "
                    + "If the user wants to change properties of the existing field, call "
                    + "plan_modify_field instead. If the user wants the existing field as-is, "
                    + "treat the requirement as satisfied and move on."
                };
                return new FieldCreationPlan(
                    PlanId: $"rejected_field_exists_{Guid.NewGuid():N}",
                    TableLogicalName: tableLogicalName,
                    FieldLogicalName: fieldLogicalName,
                    FieldSchemaName: fieldSchemaName,
                    DisplayName: displayName,
                    Description: description,
                    FieldType: fieldType,
                    RequiredLevel: requiredLevel,
                    IsSearchable: isSearchable,
                    IsAuditEnabled: isAuditEnabled,
                    Warnings: rejection.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);
            }

            if (conflicts.Count > 0)
            {
                warnings.Add("Possible duplicate — confirm with the user before creating:");
                warnings.AddRange(conflicts);
            }
        }
        catch (Exception)
        {
            // Non-fatal — if we can't read the table (network, table doesn't exist yet),
            // proceed with the plan. Execute will surface real errors.
        }

        // Build OData payload based on field type
        var payload = new Dictionary<string, object>();

        switch (fieldType.ToLowerInvariant())
        {
            case "string":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata";
                payload["MaxLength"] = GetConfigInt(config, "maxLength", 200);
                payload["FormatName"] = new Dictionary<string, object>
                {
                    ["Value"] = GetConfigString(config, "format", "Text")
                };
                break;

            case "memo":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.MemoAttributeMetadata";
                payload["MaxLength"] = GetConfigInt(config, "maxLength", 2000);
                payload["FormatName"] = new Dictionary<string, object>
                {
                    ["Value"] = GetConfigString(config, "format", "Text")
                };
                break;

            case "integer":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata";
                payload["MinValue"] = GetConfigInt(config, "minValue", -2147483648);
                payload["MaxValue"] = GetConfigInt(config, "maxValue", 2147483647);
                payload["Format"] = GetConfigString(config, "format", "None");
                break;

            case "decimal":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata";
                payload["Precision"] = GetConfigInt(config, "precision", 2);
                payload["MinValue"] = GetConfigDecimal(config, "minValue", -100000000000m);
                payload["MaxValue"] = GetConfigDecimal(config, "maxValue", 100000000000m);
                break;

            case "float":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.DoubleAttributeMetadata";
                payload["Precision"] = GetConfigInt(config, "precision", 2);
                payload["MinValue"] = GetConfigDouble(config, "minValue", 0);
                payload["MaxValue"] = GetConfigDouble(config, "maxValue", 1000000000);
                break;

            case "money":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.MoneyAttributeMetadata";
                payload["Precision"] = GetConfigInt(config, "precision", 2);
                payload["PrecisionSource"] = GetConfigInt(config, "precisionSource", 2);
                break;

            case "boolean":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata";
                payload["OptionSet"] = new Dictionary<string, object>
                {
                    ["TrueOption"] = new Dictionary<string, object>
                    {
                        ["Value"] = 1,
                        ["Label"] = ToolHelpers.BuildLocalizedLabel(GetConfigString(config, "trueLabel", "Yes"))
                    },
                    ["FalseOption"] = new Dictionary<string, object>
                    {
                        ["Value"] = 0,
                        ["Label"] = ToolHelpers.BuildLocalizedLabel(GetConfigString(config, "falseLabel", "No"))
                    }
                };
                break;

            case "datetime":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata";
                payload["Format"] = GetConfigString(config, "format", "DateOnly");
                payload["DateTimeBehavior"] = new Dictionary<string, object>
                {
                    ["Value"] = GetConfigString(config, "behavior", "UserLocal")
                };
                break;

            case "localoptionset":
                // Code guard: Choice fields default to GLOBAL option sets. Local picklists
                // are non-reusable and a legacy pattern. Refuse unless the agent explicitly
                // opts in via the `allowLocalOptionSet` config flag.
                if (!GetConfigBool(config, "allowLocalOptionSet", false))
                {
                    var rejectionWarnings = new List<string>
                    {
                        "REJECTED: Choice fields must use a GLOBAL option set, not local. "
                        + "Call plan_create_global_option_set first, then call plan_create_field with "
                        + "fieldType='globalOptionSet' and fieldTypeConfigJson='{\"globalOptionSetName\":\"<name>\"}'. "
                        + "If the user explicitly insisted on a local picklist (rare), pass "
                        + "fieldTypeConfigJson with allowLocalOptionSet:true to override this guard."
                    };
                    return new FieldCreationPlan(
                        PlanId: $"rejected_local_optionset_{Guid.NewGuid():N}",
                        TableLogicalName: tableLogicalName,
                        FieldLogicalName: fieldLogicalName,
                        FieldSchemaName: fieldSchemaName,
                        DisplayName: displayName,
                        Description: description,
                        FieldType: fieldType,
                        RequiredLevel: requiredLevel,
                        IsSearchable: isSearchable,
                        IsAuditEnabled: isAuditEnabled,
                        Warnings: rejectionWarnings.AsReadOnly(),
                        RiskLevel: PlanRisk.Red);
                }
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata";
                payload["OptionSet"] = BuildLocalOptionSet(config, fieldSchemaName);
                break;

            case "localmultiselectoptionset":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata";
                payload["OptionSet"] = BuildLocalOptionSet(config, fieldSchemaName);
                break;

            case "globaloptionset":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata";
                var globalName = GetConfigString(config, "globalOptionSetName", "");
                if (string.IsNullOrWhiteSpace(globalName))
                {
                    warnings.Add("globalOptionSetName is required for globalOptionSet type.");
                }
                else
                {
                    // Resolve to MetadataId at plan time. Dataverse rejects the
                    // (Name='x') form on PATCH/POST with a generic "Guid should
                    // contain 32 digits" 500 error — the MetadataId GUID form is
                    // the only reliable bind syntax.
                    var resolvedId = await dataverseClient.ResolveGlobalOptionSetMetadataIdAsync(globalName, cancellationToken);
                    if (resolvedId is null)
                    {
                        var rejection = new List<string>
                        {
                            $"REJECTED: Global option set '{globalName}' does not exist in this environment. "
                            + "Call get_global_option_sets to find the correct Name (the LOGICAL name, "
                            + "not display name), or call plan_create_global_option_set first to create it. "
                            + "DO NOT execute this plan."
                        };
                        return new FieldCreationPlan(
                            PlanId: $"rejected_optionset_missing_{Guid.NewGuid():N}",
                            TableLogicalName: tableLogicalName,
                            FieldLogicalName: fieldLogicalName,
                            FieldSchemaName: fieldSchemaName,
                            DisplayName: displayName,
                            Description: description,
                            FieldType: fieldType,
                            RequiredLevel: requiredLevel,
                            IsSearchable: isSearchable,
                            IsAuditEnabled: isAuditEnabled,
                            Warnings: rejection.AsReadOnly(),
                            RiskLevel: PlanRisk.Red);
                    }
                    payload["GlobalOptionSet@odata.bind"] = $"/GlobalOptionSetDefinitions({resolvedId.Value})";
                }
                break;

            case "lookup":
                // Hard reject — lookup attributes CANNOT be created through
                // EntityDefinitions/Attributes (Dataverse responds with "lookup
                // attributes cannot be created through the SDK in this way").
                // The only supported path is the CreateOneToMany Web API action,
                // which atomically creates the relationship + the lookup column
                // on the referencing entity.
                return new FieldCreationPlan(
                    PlanId: $"rejected_lookup_field_{Guid.NewGuid():N}",
                    TableLogicalName: tableLogicalName,
                    FieldLogicalName: fieldLogicalName,
                    FieldSchemaName: fieldSchemaName,
                    DisplayName: displayName,
                    Description: description,
                    FieldType: fieldType,
                    RequiredLevel: requiredLevel,
                    IsSearchable: isSearchable,
                    IsAuditEnabled: isAuditEnabled,
                    Warnings: new List<string>
                    {
                        "REJECTED: Lookup fields cannot be created via plan_create_field. "
                        + "Dataverse only allows lookup creation through the CreateOneToMany action. "
                        + "Call plan_create_one_to_many_relationship instead — it creates the "
                        + "1:N relationship AND the lookup column on the referencing entity in one shot. "
                        + "Pass referencedEntity (the parent table), referencingEntity (this table), "
                        + "and a schemaName like '<prefix>_<parent>_<child>'."
                    }.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);

            case "customer":
                // Customer fields go through the dedicated CreateCustomerRelationships
                // action — Dataverse does NOT accept Customer columns through the
                // generic Attributes endpoint. Redirect to the proper tool rather
                // than sending a doomed payload.
                return new FieldCreationPlan(
                    PlanId: $"rejected_customer_field_{Guid.NewGuid():N}",
                    TableLogicalName: tableLogicalName,
                    FieldLogicalName: fieldLogicalName,
                    FieldSchemaName: fieldSchemaName,
                    DisplayName: displayName,
                    Description: description,
                    FieldType: fieldType,
                    RequiredLevel: requiredLevel,
                    IsSearchable: isSearchable,
                    IsAuditEnabled: isAuditEnabled,
                    Warnings: new List<string>
                    {
                        "REJECTED: Customer columns must go through the CreateCustomerRelationships "
                        + "action — they cannot be created through plan_create_field. "
                        + "Use plan_create_customer_lookup instead. It atomically creates the column "
                        + "plus the two 1:N relationships (to Account and Contact) with the proper "
                        + "Customer column type that the maker portal recognizes."
                    }.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);

            case "file":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.FileAttributeMetadata";
                payload["MaxSizeInKB"] = GetConfigInt(config, "maxSizeInKB", 32768);
                break;

            case "image":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.ImageAttributeMetadata";
                payload["MaxSizeInKB"] = GetConfigInt(config, "maxSizeInKB", 10240);
                payload["CanStoreFullImage"] = GetConfigBool(config, "canStoreFullImage", true);
                break;

            case "autonumber":
                // Autonumber is a String attribute under the hood with an
                // AutoNumberFormat — MaxLength is REQUIRED on String, even when
                // the actual value is generated. Past failure: omitting MaxLength
                // returned "string attributes require a max length value" 400.
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata";
                payload["MaxLength"] = GetConfigInt(config, "maxLength", 100);
                payload["AutoNumberFormat"] = GetConfigString(config, "format", "{SEQNUM:6}");
                payload["FormatName"] = new Dictionary<string, object> { ["Value"] = "Text" };
                break;

            case "calculated":
                // Calculated columns need a real FormulaDefinition to be useful,
                // and the Web API doesn't accept it on attribute create. The
                // resulting column was a String placeholder that confused
                // everyone. Refuse cleanly and point at the maker portal.
                return new FieldCreationPlan(
                    PlanId: $"rejected_calculated_field_{Guid.NewGuid():N}",
                    TableLogicalName: tableLogicalName,
                    FieldLogicalName: fieldLogicalName,
                    FieldSchemaName: fieldSchemaName,
                    DisplayName: displayName,
                    Description: description,
                    FieldType: fieldType,
                    RequiredLevel: requiredLevel,
                    IsSearchable: isSearchable,
                    IsAuditEnabled: isAuditEnabled,
                    Warnings: new List<string>
                    {
                        "REJECTED: Calculated fields aren't supported in this app. "
                        + "The Dataverse Web API doesn't accept FormulaDefinition on "
                        + "attribute create — you'd end up with an empty placeholder "
                        + "column the user still has to finish in the portal. "
                        + "Tell the user to build it in the Power Apps maker portal "
                        + $"(Tables → {tableLogicalName} → New column → Calculated)."
                    }.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);

            case "rollup":
                // Rollup creates an Integer placeholder column (SourceType=2).
                // The actual rollup formula (Source / Related entity / Aggregation
                // function) is configured visually in the maker portal — the
                // Web API doesn't expose a stable schema for the rollup
                // FormulaDefinition XML, so we don't try to set it here.
                warnings.Add(
                    "Rollup field will be created as a PLACEHOLDER. Source/Related/"
                    + "Aggregation are empty until you configure them in the maker portal. "
                    + "Steps: Tables → " + tableLogicalName + " → Columns → "
                    + "<the new column> → Edit → set Source entity, Related entity, "
                    + "and Aggregation (COUNT, SUM, MIN, MAX, or AVG) → Save. "
                    + "Reference: https://learn.microsoft.com/en-us/power-apps/maker/data-platform/define-rollup-fields");
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata";
                payload["SourceType"] = 2; // Rollup
                break;

            default:
                warnings.Add($"Unknown field type '{fieldType}'. Defaulting to StringAttributeMetadata.");
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata";
                payload["MaxLength"] = 200;
                payload["FormatName"] = new Dictionary<string, object> { ["Value"] = "Text" };
                break;
        }

        // Common properties
        payload["SchemaName"] = fieldSchemaName;
        payload["DisplayName"] = ToolHelpers.BuildLocalizedLabel(displayName);
        payload["Description"] = ToolHelpers.BuildLocalizedLabel(description);
        payload["RequiredLevel"] = new Dictionary<string, object> { ["Value"] = requiredLevel, ["CanBeChanged"] = true };
        payload["IsAuditEnabled"] = new Dictionary<string, object> { ["Value"] = isAuditEnabled, ["CanBeChanged"] = true };

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"create_field_{Guid.NewGuid():N}";

        // Persist payload server-side; ExecuteCreateField looks it up by planId so the agent
        // can't reshape `IsAuditEnabled` into a primitive boolean.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new FieldCreationPlan(
            PlanId: planId,
            TableLogicalName: tableLogicalName,
            FieldLogicalName: fieldLogicalName,
            FieldSchemaName: fieldSchemaName,
            DisplayName: displayName,
            Description: description,
            FieldType: fieldType,
            RequiredLevel: requiredLevel,
            IsSearchable: isSearchable,
            IsAuditEnabled: isAuditEnabled,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Yellow);
    }

    private static Dictionary<string, object> BuildLocalOptionSet(Dictionary<string, JsonElement> config, string schemaName)
    {
        var options = new List<Dictionary<string, object>>();

        if (config.TryGetValue("options", out var optionsElement) && optionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var opt in optionsElement.EnumerateArray())
            {
                var label = opt.TryGetProperty("label", out var lbl) ? lbl.GetString() ?? "" : "";
                var value = opt.TryGetProperty("value", out var val) ? val.GetInt32() : 0;
                var entry = new Dictionary<string, object>
                {
                    ["Value"] = value,
                    ["Label"] = ToolHelpers.BuildLocalizedLabel(label)
                };
                // Optional Color (#RRGGBB) — Dataverse renders option as a colored badge.
                if (opt.TryGetProperty("color", out var colorEl) && colorEl.ValueKind == JsonValueKind.String)
                {
                    var color = colorEl.GetString();
                    if (ToolHelpers.LooksLikeHexColor(color))
                        entry["Color"] = color!;
                }
                options.Add(entry);
            }
        }

        return new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadata",
            ["IsGlobal"] = false,
            ["OptionSetType"] = "Picklist",
            ["Name"] = schemaName.ToLowerInvariant(),
            ["Options"] = options
        };
    }

    private static int GetConfigInt(Dictionary<string, JsonElement> config, string key, int defaultValue) =>
        config.TryGetValue(key, out var el) && el.TryGetInt32(out var val) ? val : defaultValue;

    private static decimal GetConfigDecimal(Dictionary<string, JsonElement> config, string key, decimal defaultValue) =>
        config.TryGetValue(key, out var el) && el.TryGetDecimal(out var val) ? val : defaultValue;

    private static double GetConfigDouble(Dictionary<string, JsonElement> config, string key, double defaultValue) =>
        config.TryGetValue(key, out var el) && el.TryGetDouble(out var val) ? val : defaultValue;

    private static bool GetConfigBool(Dictionary<string, JsonElement> config, string key, bool defaultValue) =>
        config.TryGetValue(key, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : defaultValue;

    private static string GetConfigString(Dictionary<string, JsonElement> config, string key, string defaultValue) =>
        config.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() ?? defaultValue : defaultValue;

}
