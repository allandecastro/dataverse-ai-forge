using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateManyToManyRelationshipTool
{
    [Description("Generate a plan for creating an N:N (many-to-many) relationship between two Dataverse tables. "
        + "Returns the full OData JSON payload, an ERD diagram fragment, and validation warnings. "
        + "Does NOT create the relationship — use execute_create_many_to_many_relationship for that.")]
    public Task<ManyToManyRelationshipCreationPlan> PlanCreateManyToManyRelationshipAsync(
        [Description("Logical name of the first table (e.g., 'cr123_student')")] string firstTable,
        [Description("Logical name of the second table (e.g., 'cr123_course')")] string secondTable,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        [Description("Optional schema name for the relationship. Auto-generated if omitted.")] string? relationshipSchemaName,
        [Description("Optional name for the intersect table. Auto-generated if omitted.")] string? intersectTableName,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var first = firstTable.Replace($"{publisherPrefix}_", "");
        var second = secondTable.Replace($"{publisherPrefix}_", "");

        var schemaName = string.IsNullOrWhiteSpace(relationshipSchemaName)
            ? $"{publisherPrefix}_{first}_{second}"
            : relationshipSchemaName;

        var intersect = string.IsNullOrWhiteSpace(intersectTableName)
            ? $"{publisherPrefix}_{first}_{second}"
            : intersectTableName;

        if (firstTable == secondTable)
            warnings.Add("Self-referencing N:N relationships require careful navigation property naming.");

        // Build OData JSON payload
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata",
            ["SchemaName"] = schemaName,
            ["IntersectEntityName"] = intersect,
            ["Entity1LogicalName"] = firstTable,
            ["Entity2LogicalName"] = secondTable,
            ["Entity1AssociatedMenuConfiguration"] = new Dictionary<string, object>
            {
                ["Behavior"] = "UseCollectionName",
                ["Group"] = "Details",
                ["Order"] = 10000
            },
            ["Entity2AssociatedMenuConfiguration"] = new Dictionary<string, object>
            {
                ["Behavior"] = "UseCollectionName",
                ["Group"] = "Details",
                ["Order"] = 10000
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var erdFragment = $"{firstTable} }}o--o{{ {secondTable} : \"{schemaName}\"";

        return Task.FromResult(new ManyToManyRelationshipCreationPlan(
            FirstTable: firstTable,
            SecondTable: secondTable,
            RelationshipSchemaName: schemaName,
            IntersectTableName: intersect,
            WebApiPayloadJson: payloadJson,
            ErDiagramFragment: erdFragment,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
    }
}
