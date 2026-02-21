using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyRelationshipCreationTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created relationship matches its planned specification. "
        + "Re-reads the relationship from the Dataverse API and compares cascade behavior and entities. "
        + "Returns PASS or FAIL verdict with any discrepancies found.")]
    public async Task<VerificationResult> VerifyRelationshipCreationAsync(
        [Description("The schema name of the relationship to verify")] string relationshipSchemaName,
        [Description("Expected parent/referenced entity logical name")] string expectedReferencedEntity,
        [Description("Expected child/referencing entity logical name")] string expectedReferencingEntity,
        [Description("Expected cascade delete behavior")] string expectedCascadeDelete,
        [Description("Expected cascade assign behavior")] string expectedCascadeAssign,
        CancellationToken cancellationToken)
    {
        RelationshipMetadata actual;
        try
        {
            actual = await dataverseClient.GetRelationshipMetadataAsync(relationshipSchemaName, cancellationToken);
        }
        catch (Exception)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("relationship", relationshipSchemaName, "NOT FOUND")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        CompareProperty(discrepancies, "ReferencedEntity", expectedReferencedEntity, actual.ReferencedEntity);
        CompareProperty(discrepancies, "ReferencingEntity", expectedReferencingEntity, actual.ReferencingEntity);
        CompareProperty(discrepancies, "CascadeDelete", expectedCascadeDelete, actual.CascadeDelete);
        CompareProperty(discrepancies, "CascadeAssign", expectedCascadeAssign, actual.CascadeAssign);

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
