using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class RunFullValidationTool(IDataverseClient dataverseClient, IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Run a full validation of all components in ProjectState. "
        + "Checks every table, field, and relationship that was created during this session. "
        + "Returns an overall verdict (PASS, PARTIAL, FAIL) with a summary.")]
    public async Task<FullValidationResult> RunFullValidationAsync(CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        var tableDetails = await ValidateTables(state.Tables, cancellationToken);
        var fieldDetails = await ValidateFields(state.Fields, cancellationToken);
        var relDetails = await ValidateRelationships(state.Relationships, cancellationToken);

        var tableSummary = BuildSummary(tableDetails);
        var fieldSummary = BuildSummary(fieldDetails);
        var relSummary = BuildSummary(relDetails);

        var totalComponents = tableSummary.Total + fieldSummary.Total + relSummary.Total;
        var totalPassed = tableSummary.Passed + fieldSummary.Passed + relSummary.Passed;
        var totalFailed = tableSummary.Failed + fieldSummary.Failed + relSummary.Failed;

        string overallVerdict;
        if (totalFailed == 0)
            overallVerdict = "PASS";
        else if (totalPassed > 0)
            overallVerdict = "PARTIAL";
        else
            overallVerdict = "FAIL";

        var summary = $"{totalPassed}/{totalComponents} components verified successfully.";
        if (totalFailed > 0)
            summary += $" {totalFailed} failed.";

        return new FullValidationResult(
            Tables: tableSummary,
            Fields: fieldSummary,
            Relationships: relSummary,
            OverallVerdict: overallVerdict,
            Summary: summary);
    }

    private async Task<List<ComponentDetail>> ValidateTables(List<CreatedTable> tables, CancellationToken cancellationToken)
    {
        var details = new List<ComponentDetail>();
        foreach (var table in tables)
        {
            try
            {
                await dataverseClient.GetTableMetadataAsync(table.LogicalName, cancellationToken);
                details.Add(new ComponentDetail(table.LogicalName, "PASS", []));
            }
            catch (Exception ex)
            {
                details.Add(new ComponentDetail(table.LogicalName, "FAIL", [$"Table not found: {ex.Message}"]));
            }
        }
        return details;
    }

    private async Task<List<ComponentDetail>> ValidateFields(List<CreatedField> fields, CancellationToken cancellationToken)
    {
        var details = new List<ComponentDetail>();
        foreach (var field in fields)
        {
            try
            {
                await dataverseClient.GetFieldMetadataAsync(field.TableLogicalName, field.LogicalName, cancellationToken);
                details.Add(new ComponentDetail($"{field.TableLogicalName}.{field.LogicalName}", "PASS", []));
            }
            catch (Exception ex)
            {
                details.Add(new ComponentDetail($"{field.TableLogicalName}.{field.LogicalName}", "FAIL", [$"Field not found: {ex.Message}"]));
            }
        }
        return details;
    }

    private async Task<List<ComponentDetail>> ValidateRelationships(List<CreatedRelationship> relationships, CancellationToken cancellationToken)
    {
        var details = new List<ComponentDetail>();
        foreach (var rel in relationships)
        {
            try
            {
                await dataverseClient.GetRelationshipMetadataAsync(rel.SchemaName, cancellationToken);
                details.Add(new ComponentDetail(rel.SchemaName, "PASS", []));
            }
            catch (Exception ex)
            {
                details.Add(new ComponentDetail(rel.SchemaName, "FAIL", [$"Relationship not found: {ex.Message}"]));
            }
        }
        return details;
    }

    private static ValidationSummary BuildSummary(List<ComponentDetail> details)
    {
        var passed = details.Count(d => d.Verdict == "PASS");
        var failed = details.Count(d => d.Verdict == "FAIL");
        return new ValidationSummary(details.Count, passed, failed, details);
    }
}
