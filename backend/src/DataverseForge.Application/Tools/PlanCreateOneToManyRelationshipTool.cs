using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateOneToManyRelationshipTool
{
    [Description("Generate a plan for creating a 1:N relationship between two Dataverse tables. "
        + "Returns the full OData JSON payload, an ERD diagram fragment, validation warnings, and risk level. "
        + "Does NOT create the relationship — use execute_create_one_to_many_relationship for that.")]
    public Task<RelationshipCreationPlan> PlanCreateOneToManyRelationshipAsync(
        [Description("Logical name of the primary (one-side) table (e.g., 'cr123_project')")] string primaryTable,
        [Description("Logical name of the related (many-side) table (e.g., 'cr123_task')")] string relatedTable,
        [Description("Display name for the lookup field on the related table (e.g., 'Project')")] string lookupFieldDisplayName,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        [Description("Cascade behavior for Assign: 'Cascade', 'Active', 'UserOwned', 'NoCascade'")] string cascadeAssign,
        [Description("Cascade behavior for Delete: 'Cascade', 'RemoveLink', 'Restrict'")] string cascadeDelete,
        [Description("Cascade behavior for Reparent: 'Cascade', 'Active', 'UserOwned', 'NoCascade'")] string cascadeReparent,
        [Description("Cascade behavior for Merge: 'Cascade', 'NoCascade'")] string cascadeMerge,
        [Description("Cascade behavior for Share: 'Cascade', 'Active', 'UserOwned', 'NoCascade'")] string cascadeShare,
        [Description("Cascade behavior for Unshare: 'Cascade', 'Active', 'UserOwned', 'NoCascade'")] string cascadeUnshare,
        [Description("Lookup behavior: 'Referential' (default), 'ReferentialRestrict', or 'Parental'")] string lookupBehavior,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Derive names
        var lookupBaseName = lookupFieldDisplayName.Replace(" ", "").ToLowerInvariant();
        var lookupFieldLogicalName = $"{publisherPrefix}_{lookupBaseName}id";
        var lookupFieldSchemaName = $"{publisherPrefix}_{lookupFieldDisplayName.Replace(" ", "")}Id";

        // Auto-generate relationship schema name if not provided
        var relationshipSchemaName = $"{publisherPrefix}_{primaryTable.Replace($"{publisherPrefix}_", "")}_{relatedTable.Replace($"{publisherPrefix}_", "")}";

        // Validate cascade delete
        if (cascadeDelete.Equals("Cascade", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Cascade delete will permanently remove all related records when the parent is deleted. Verify this is intended.");

        if (lookupBehavior.Equals("Parental", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Parental behavior enforces cascading on assign, share, unshare, and reparent operations.");

        // Build OData JSON payload
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata",
            ["SchemaName"] = relationshipSchemaName,
            ["ReferencedEntity"] = primaryTable,
            ["ReferencingEntity"] = relatedTable,
            ["Lookup"] = new Dictionary<string, object>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
                ["SchemaName"] = lookupFieldSchemaName,
                ["DisplayName"] = BuildLocalizedLabel(lookupFieldDisplayName),
                ["RequiredLevel"] = new Dictionary<string, object> { ["Value"] = "None", ["CanBeChanged"] = true }
            },
            ["CascadeConfiguration"] = new Dictionary<string, object>
            {
                ["Assign"] = cascadeAssign,
                ["Delete"] = cascadeDelete,
                ["Reparent"] = cascadeReparent,
                ["Merge"] = cascadeMerge,
                ["Share"] = cascadeShare,
                ["Unshare"] = cascadeUnshare
            },
            ["AssociatedMenuConfiguration"] = new Dictionary<string, object>
            {
                ["Behavior"] = "UseCollectionName",
                ["Group"] = "Details",
                ["Order"] = 10000
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        // Generate ERD fragment (Mermaid erDiagram syntax)
        var erdFragment = $"{primaryTable} ||--o{{ {relatedTable} : \"{relationshipSchemaName}\"";

        return Task.FromResult(new RelationshipCreationPlan(
            PrimaryTable: primaryTable,
            RelatedTable: relatedTable,
            LookupFieldLogicalName: lookupFieldLogicalName,
            LookupFieldDisplayName: lookupFieldDisplayName,
            RelationshipSchemaName: relationshipSchemaName,
            CascadeAssign: cascadeAssign,
            CascadeDelete: cascadeDelete,
            CascadeReparent: cascadeReparent,
            CascadeMerge: cascadeMerge,
            CascadeShare: cascadeShare,
            CascadeUnshare: cascadeUnshare,
            LookupBehavior: lookupBehavior,
            WebApiPayloadJson: payloadJson,
            ErDiagramFragment: erdFragment,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
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
