using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateAlternateKeyTool
{
    [Description("Generate a plan for creating an alternate key on a Dataverse table. "
        + "Alternate keys enable upsert operations and enforce uniqueness. Max 5 attributes per key. "
        + "Returns the full OData JSON payload and validation warnings. "
        + "Does NOT create the key — use execute_create_alternate_key for that.")]
    public Task<AlternateKeyCreationPlan> PlanCreateAlternateKeyAsync(
        [Description("Logical name of the table (e.g., 'cr123_project')")] string tableLogicalName,
        [Description("Display name for the alternate key (e.g., 'External Project ID')")] string keyDisplayName,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        [Description("Comma-separated list of field logical names for the key (e.g., 'cr123_externalid,cr123_source')")] string keyAttributesCsv,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var keyAttributes = keyAttributesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (keyAttributes.Count == 0)
            warnings.Add("At least one key attribute is required.");

        if (keyAttributes.Count > 5)
            warnings.Add("Dataverse alternate keys support a maximum of 5 attributes. This key will fail to create.");

        var baseName = keyDisplayName.Replace(" ", "").ToLowerInvariant();
        var keyLogicalName = $"{publisherPrefix}_{baseName}key";

        // Build OData JSON payload
        var payload = new Dictionary<string, object>
        {
            ["LogicalName"] = keyLogicalName,
            ["DisplayName"] = ToolHelpers.BuildLocalizedLabel(keyDisplayName),
            ["KeyAttributes"] = keyAttributes
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new AlternateKeyCreationPlan(
            TableLogicalName: tableLogicalName,
            KeyLogicalName: keyLogicalName,
            KeyDisplayName: keyDisplayName,
            KeyAttributes: keyAttributes.AsReadOnly(),
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow"));
    }

}
