using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyFieldCreationTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created field matches its planned specification. "
        + "Re-reads the field from the Dataverse API and compares type, constraints, searchable, and required level. "
        + "Returns PASS or FAIL verdict with any discrepancies found.")]
    public async Task<FieldVerificationResult> VerifyFieldCreationAsync(
        [Description("The logical name of the table the field belongs to")] string tableLogicalName,
        [Description("The logical name of the field to verify")] string fieldLogicalName,
        [Description("Expected field type (e.g. StringType, IntegerType, BooleanType)")] string expectedFieldType,
        [Description("Expected required level (None, ApplicationRequired, Recommended)")] string expectedRequiredLevel,
        [Description("Expected IsSearchable setting")] bool expectedIsSearchable,
        CancellationToken cancellationToken)
    {
        FieldMetadata actual;
        try
        {
            actual = await dataverseClient.GetFieldMetadataAsync(tableLogicalName, fieldLogicalName, cancellationToken);
        }
        catch (Exception)
        {
            return new FieldVerificationResult(
                Exists: false,
                TypeMatches: false,
                ConstraintsMatch: false,
                Discrepancies: [new Discrepancy("field", fieldLogicalName, "NOT FOUND")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        var typeMatches = string.Equals(expectedFieldType, actual.AttributeType, StringComparison.OrdinalIgnoreCase);
        if (!typeMatches)
        {
            discrepancies.Add(new Discrepancy("AttributeType", expectedFieldType, actual.AttributeType));
        }

        var requiredMatches = string.Equals(expectedRequiredLevel, actual.RequiredLevel, StringComparison.OrdinalIgnoreCase);
        if (!requiredMatches)
        {
            discrepancies.Add(new Discrepancy("RequiredLevel", expectedRequiredLevel, actual.RequiredLevel));
        }

        var searchableMatches = expectedIsSearchable == actual.IsSearchable;
        if (!searchableMatches)
        {
            discrepancies.Add(new Discrepancy("IsSearchable", expectedIsSearchable.ToString(), actual.IsSearchable.ToString()));
        }

        var constraintsMatch = requiredMatches && searchableMatches;
        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";

        return new FieldVerificationResult(
            Exists: true,
            TypeMatches: typeMatches,
            ConstraintsMatch: constraintsMatch,
            Discrepancies: discrepancies,
            Verdict: verdict);
    }
}
