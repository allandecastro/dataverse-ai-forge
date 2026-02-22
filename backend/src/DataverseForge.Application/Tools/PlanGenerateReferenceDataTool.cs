using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanGenerateReferenceDataTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Suggest appropriate reference/seed data values for a table based on its fields. "
        + "Analyzes the table definition in project state and generates sensible default records. "
        + "Returns suggested records that can be reviewed and passed to plan_populate_reference_data.")]
    public async Task<SuggestedReferenceData> PlanGenerateReferenceDataAsync(
        [Description("The logical name of the target table")] string tableLogicalName,
        [Description("Number of records to suggest (default: 5-10)")] int recordCount,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (recordCount <= 0) recordCount = 5;

        var notes = new List<string>();
        var suggestedRecords = new List<Dictionary<string, object?>>();

        var table = state.Tables.FirstOrDefault(t => t.LogicalName == tableLogicalName);
        if (table == null)
        {
            notes.Add($"Table '{tableLogicalName}' not found in project state. Cannot generate suggestions.");
            return new SuggestedReferenceData(tableLogicalName, 0, suggestedRecords.AsReadOnly(), notes.AsReadOnly());
        }

        var tableFields = state.Fields
            .Where(f => f.TableLogicalName == tableLogicalName)
            .ToList();

        if (tableFields.Count == 0)
        {
            notes.Add("No custom fields found for this table. Only the primary name field may be available.");
        }

        // Generate placeholder records based on field definitions
        var nameField = tableFields.FirstOrDefault(f =>
            f.LogicalName.EndsWith("_name", StringComparison.OrdinalIgnoreCase)
            || f.FieldType.Equals("String", StringComparison.OrdinalIgnoreCase));

        for (var i = 1; i <= recordCount; i++)
        {
            var record = new Dictionary<string, object?>();
            if (nameField != null)
            {
                record[nameField.LogicalName] = $"{table.DisplayName} {i}";
            }

            foreach (var field in tableFields)
            {
                if (record.ContainsKey(field.LogicalName)) continue;

                switch (field.FieldType.ToLowerInvariant())
                {
                    case "integer" or "wholenumber":
                        record[field.LogicalName] = i * 10;
                        break;
                    case "boolean" or "twooptionset":
                        record[field.LogicalName] = i % 2 == 0;
                        break;
                    case "decimal" or "money" or "currency":
                        record[field.LogicalName] = i * 10.5m;
                        break;
                }
            }

            suggestedRecords.Add(record);
        }

        notes.Add($"Generated {suggestedRecords.Count} suggested records based on table '{table.DisplayName}' definition.");
        notes.Add("Review and modify these suggestions, then pass them to plan_populate_reference_data for execution.");

        return new SuggestedReferenceData(
            tableLogicalName,
            suggestedRecords.Count,
            suggestedRecords.AsReadOnly(),
            notes.AsReadOnly());
    }
}
