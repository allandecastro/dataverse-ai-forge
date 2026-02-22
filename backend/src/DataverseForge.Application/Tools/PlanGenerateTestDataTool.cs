using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanGenerateTestDataTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Plan generation of realistic test data records for a table. "
        + "Generates varied, locale-aware test records with distributed values. "
        + "Records are tagged with '[TEST DATA]' for later cleanup. "
        + "Risk level: yellow (<20 records), orange (>=20 records).")]
    public async Task<TestDataPlan> PlanGenerateTestDataAsync(
        [Description("The logical name of the target table")] string tableLogicalName,
        [Description("The entity set name for the OData endpoint")] string entitySetName,
        [Description("Number of test records to generate (default: 10)")] int recordCount,
        [Description("JSON array of record objects with field values")] string recordsJson,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var warnings = new List<string>();

        if (recordCount <= 0) recordCount = 10;

        List<Dictionary<string, object?>> records;
        try
        {
            records = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(recordsJson) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            records = [];
            warnings.Add("Could not parse records JSON. Please provide a valid JSON array.");
        }

        // Validate table exists
        var table = state.Tables.FirstOrDefault(t => t.LogicalName == tableLogicalName);
        if (table == null)
        {
            warnings.Add($"Table '{tableLogicalName}' not found in project state.");
        }

        // Check for lookup fields that need existing records
        var lookupFields = state.Fields
            .Where(f => f.TableLogicalName == tableLogicalName
                && f.FieldType.Equals("Lookup", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (lookupFields.Count > 0 && state.SeedDataRecords.Count == 0)
        {
            warnings.Add($"Table has {lookupFields.Count} lookup field(s) but no seed data exists. Consider populating reference tables first.");
        }

        // Build record payloads
        var recordPayloads = records
            .Select(r => System.Text.Json.JsonSerializer.Serialize(r))
            .ToList();

        var batchPayload = System.Text.Json.JsonSerializer.Serialize(new { entitySetName, records = recordPayloads });

        var riskLevel = recordCount >= 20 ? "orange" : "yellow";

        if (recordCount >= 50)
        {
            warnings.Add($"Generating {recordCount} records. Consider batching in smaller sets for reliability.");
        }

        return new TestDataPlan(
            TableLogicalName: tableLogicalName,
            EntitySetName: entitySetName,
            RecordCount: records.Count,
            Records: records.AsReadOnly(),
            WebApiBatchPayloadJson: batchPayload,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: riskLevel);
    }
}
