using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanPopulateReferenceDataTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Plan batch creation of reference/seed data records for a table. "
        + "Validates records against table fields in project state, generates OData batch payload, "
        + "and returns warnings for missing required fields. Risk level: yellow.")]
    public async Task<ReferenceDataPlan> PlanPopulateReferenceDataAsync(
        [Description("The logical name of the target table")] string tableLogicalName,
        [Description("The entity set name for the OData endpoint (e.g., 'cr123_feedbackcategories')")] string entitySetName,
        [Description("JSON array of record objects with field values, e.g. [{\"cr123_name\":\"Product Quality\"}]")] string recordsJson,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var warnings = new List<string>();

        List<Dictionary<string, object?>> records;
        try
        {
            records = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(recordsJson) ?? [];
        }
        catch (JsonException)
        {
            records = [];
            warnings.Add("Could not parse records JSON. Please provide a valid JSON array.");
        }

        if (records.Count == 0)
        {
            warnings.Add("No records provided. Please supply at least one record.");
        }

        // Check if table exists in project state
        var table = state.Tables.FirstOrDefault(t => t.LogicalName == tableLogicalName);
        if (table == null)
        {
            warnings.Add($"Table '{tableLogicalName}' not found in project state. Verify the table has been created.");
        }

        // Check required fields
        var requiredFields = state.Fields
            .Where(f => f.TableLogicalName == tableLogicalName && f.FieldType.Contains("Required", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.LogicalName)
            .ToList();

        foreach (var record in records)
        {
            foreach (var reqField in requiredFields)
            {
                if (!record.ContainsKey(reqField))
                {
                    warnings.Add($"Record is missing required field '{reqField}'.");
                    break;
                }
            }
        }

        // Build individual record payloads for batch
        var recordPayloads = records
            .Select(r => JsonSerializer.Serialize(r))
            .ToList();

        var batchPayload = JsonSerializer.Serialize(new { entitySetName, records = recordPayloads });

        // planId-by-reference (CLAUDE.md recurring bug #1). The executor will
        // look up batchPayload by planId so the agent never re-passes it.
        // Reject paths use a "rejected_*" planId so the executor can refuse
        // to act on them.
        var rejected = warnings.Any(w => w.StartsWith("Could not parse", StringComparison.OrdinalIgnoreCase))
            || records.Count == 0;
        var planId = rejected
            ? $"rejected_reference_data_invalid_{Guid.NewGuid():N}"
            : $"populate_reference_data_{Guid.NewGuid():N}";
        if (!rejected)
        {
            state.AddPendingPlan(planId, batchPayload);
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
        }

        return new ReferenceDataPlan(
            PlanId: planId,
            TableLogicalName: tableLogicalName,
            EntitySetName: entitySetName,
            RecordCount: records.Count,
            Records: records.AsReadOnly(),
            WebApiBatchPayloadJson: batchPayload,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow");
    }
}
