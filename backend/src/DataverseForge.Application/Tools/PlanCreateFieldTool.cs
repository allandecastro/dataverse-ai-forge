using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateFieldTool
{
    [Description("Generate a plan for creating a new field (column) on a Dataverse table. Returns the full OData JSON payload, "
        + "validation warnings, and risk level. Does NOT create the field — use execute_create_field for that.")]
    public Task<FieldCreationPlan> PlanCreateFieldAsync(
        [Description("Logical name of the target table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("Display name for the field (e.g., 'Priority')")] string displayName,
        [Description("Description of the field")] string description,
        [Description("Field type: string, memo, integer, decimal, float, money, boolean, dateTime, "
            + "localOptionSet, localMultiSelectOptionSet, globalOptionSet, lookup, customer, file, image, autoNumber, "
            + "calculated, rollup")] string fieldType,
        [Description("JSON string with type-specific configuration (e.g., '{\"maxLength\":200}' for string, "
            + "'{\"options\":[{\"label\":\"Low\",\"value\":100000000},{\"label\":\"High\",\"value\":100000001}]}' for localOptionSet)")] string fieldTypeConfigJson,
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

        // Derive logical and schema names
        var baseName = displayName.Replace(" ", "").ToLowerInvariant();
        var fieldLogicalName = $"{publisherPrefix}_{baseName}";
        var fieldSchemaName = $"{publisherPrefix}_{displayName.Replace(" ", "")}";

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
                    warnings.Add("globalOptionSetName is required for globalOptionSet type.");
                payload["GlobalOptionSet@odata.bind"] = $"/GlobalOptionSetDefinitions(Name='{globalName}')";
                break;

            case "lookup":
                warnings.Add("Lookup fields are created as a side effect of 1:N relationships. "
                    + "Use plan_create_one_to_many_relationship instead of creating a lookup field directly.");
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.LookupAttributeMetadata";
                break;

            case "customer":
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.LookupAttributeMetadata";
                payload["Targets"] = new[] { "account", "contact" };
                break;

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
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata";
                payload["AutoNumberFormat"] = GetConfigString(config, "format", "{SEQNUM:6}");
                payload["FormatName"] = new Dictionary<string, object> { ["Value"] = "Text" };
                break;

            case "calculated":
                warnings.Add("Calculated fields require post-creation formula configuration via the customization UI. "
                    + "The field will be created as a placeholder.");
                payload["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata";
                payload["FormatName"] = new Dictionary<string, object> { ["Value"] = "Text" };
                payload["SourceType"] = 1; // Calculated
                break;

            case "rollup":
                warnings.Add("Rollup fields require post-creation formula configuration via the customization UI. "
                    + "The field will be created as a placeholder.");
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

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new FieldCreationPlan(
            TableLogicalName: tableLogicalName,
            FieldLogicalName: fieldLogicalName,
            FieldSchemaName: fieldSchemaName,
            DisplayName: displayName,
            Description: description,
            FieldType: fieldType,
            RequiredLevel: requiredLevel,
            IsSearchable: isSearchable,
            IsAuditEnabled: isAuditEnabled,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow"));
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
                options.Add(new Dictionary<string, object>
                {
                    ["Value"] = value,
                    ["Label"] = ToolHelpers.BuildLocalizedLabel(label)
                });
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
