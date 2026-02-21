using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyTableCreationTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created table matches its planned specification. "
        + "Re-reads the table from the Dataverse API and compares every property against the expected spec. "
        + "Returns PASS, FAIL, or PARTIAL verdict with any discrepancies found.")]
    public async Task<VerificationResult> VerifyTableCreationAsync(
        [Description("The logical name of the table to verify")] string tableLogicalName,
        [Description("Expected display name")] string expectedDisplayName,
        [Description("Expected ownership type (UserOwned or OrganizationOwned)")] string expectedOwnershipType,
        [Description("Expected HasNotes setting")] bool expectedHasNotes,
        [Description("Expected HasActivities setting")] bool expectedHasActivities,
        [Description("Expected ChangeTrackingEnabled setting")] bool expectedChangeTrackingEnabled,
        CancellationToken cancellationToken)
    {
        TableMetadata actual;
        try
        {
            actual = await dataverseClient.GetTableMetadataAsync(tableLogicalName, cancellationToken);
        }
        catch (Exception)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("table", tableLogicalName, "NOT FOUND")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        CompareProperty(discrepancies, "DisplayName", expectedDisplayName, actual.DisplayName);
        CompareProperty(discrepancies, "OwnershipType", expectedOwnershipType, actual.OwnershipType);
        CompareProperty(discrepancies, "HasNotes", expectedHasNotes.ToString(), actual.HasNotes.ToString());
        CompareProperty(discrepancies, "HasActivities", expectedHasActivities.ToString(), actual.HasActivities.ToString());
        CompareProperty(discrepancies, "ChangeTrackingEnabled", expectedChangeTrackingEnabled.ToString(), actual.ChangeTrackingEnabled.ToString());

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";
        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: verdict);
    }

    private static void CompareProperty(List<Discrepancy> discrepancies, string property, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            discrepancies.Add(new Discrepancy(property, expected, actual));
        }
    }
}
