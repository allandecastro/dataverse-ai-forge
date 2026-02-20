using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateTableTool
{
    [Description("Generate a plan for creating a new Dataverse table. Returns the full OData JSON payload, "
        + "validation warnings, and risk level. Does NOT create the table — use execute_create_table for that.")]
    public Task<TableCreationPlan> PlanCreateTableAsync(
        [Description("The display name for the table (e.g., 'Work Order')")] string displayName,
        [Description("The plural display name (e.g., 'Work Orders')")] string pluralDisplayName,
        [Description("Description of what the table represents")] string description,
        [Description("Ownership type: 'UserOwned' or 'OrganizationOwned'")] string ownershipType,
        [Description("Display name for the primary name field (e.g., 'Work Order Title')")] string primaryFieldDisplayName,
        [Description("Max length for the primary name field (default 100, max 4000)")] int primaryFieldMaxLength,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        [Description("Solution unique name to add the table to")] string solutionUniqueName,
        [Description("Enable notes/attachments on the table")] bool hasNotes,
        [Description("Enable activities on the table")] bool hasActivities,
        [Description("Enable change tracking")] bool changeTrackingEnabled,
        [Description("Enable audit on the table")] bool isAuditEnabled,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Derive logical and schema names
        var baseName = displayName.Replace(" ", "").ToLowerInvariant();
        var tableLogicalName = $"{publisherPrefix}_{baseName}";
        var tableSchemaName = $"{publisherPrefix}_{displayName.Replace(" ", "")}";
        var primaryFieldLogicalName = $"{publisherPrefix}_{baseName}name";
        var primaryFieldSchemaName = $"{publisherPrefix}_{displayName.Replace(" ", "")}Name";

        // Validate
        if (tableLogicalName.Length > 128)
            warnings.Add("Table logical name exceeds 128 characters — it will be truncated by Dataverse.");

        if (primaryFieldMaxLength < 1 || primaryFieldMaxLength > 4000)
        {
            warnings.Add($"Primary field max length {primaryFieldMaxLength} is out of range (1–4000). Defaulting to 100.");
            primaryFieldMaxLength = 100;
        }

        if (ownershipType is not ("UserOwned" or "OrganizationOwned"))
            warnings.Add($"Ownership type '{ownershipType}' is not standard. Expected 'UserOwned' or 'OrganizationOwned'.");

        if (hasActivities)
            warnings.Add("Enabling activities adds system relationships and cannot be reversed after publish.");

        // Build OData JSON payload
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata",
            ["SchemaName"] = tableSchemaName,
            ["DisplayName"] = BuildLocalizedLabel(displayName),
            ["DisplayCollectionName"] = BuildLocalizedLabel(pluralDisplayName),
            ["Description"] = BuildLocalizedLabel(description),
            ["OwnershipType"] = ownershipType,
            ["HasNotes"] = hasNotes,
            ["HasActivities"] = hasActivities,
            ["ChangeTrackingEnabled"] = changeTrackingEnabled,
            ["IsAuditEnabled"] = new Dictionary<string, object> { ["Value"] = isAuditEnabled, ["CanBeChanged"] = true },
            ["Attributes"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.StringAttributeMetadata",
                    ["SchemaName"] = primaryFieldSchemaName,
                    ["DisplayName"] = BuildLocalizedLabel(primaryFieldDisplayName),
                    ["IsPrimaryName"] = true,
                    ["RequiredLevel"] = new Dictionary<string, object> { ["Value"] = "ApplicationRequired", ["CanBeChanged"] = true },
                    ["MaxLength"] = primaryFieldMaxLength,
                    ["FormatName"] = new Dictionary<string, object> { ["Value"] = "Text" }
                }
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new TableCreationPlan(
            TableLogicalName: tableLogicalName,
            TableSchemaName: tableSchemaName,
            DisplayName: displayName,
            PluralDisplayName: pluralDisplayName,
            Description: description,
            OwnershipType: ownershipType,
            PrimaryFieldLogicalName: primaryFieldLogicalName,
            PrimaryFieldDisplayName: primaryFieldDisplayName,
            PrimaryFieldMaxLength: primaryFieldMaxLength,
            HasNotes: hasNotes,
            HasActivities: hasActivities,
            ChangeTrackingEnabled: changeTrackingEnabled,
            IsAuditEnabled: isAuditEnabled,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange",
            SolutionUniqueName: solutionUniqueName));
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
