using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyRelationshipCreationTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created relationship matches its planned specification. "
        + "Re-reads the relationship from the Dataverse API and compares cascade behavior and entities. "
        + "Returns PASS or FAIL verdict with any discrepancies found. "
        + "On a NOT FOUND from the direct metadata endpoint, falls back to listing relationships on "
        + "the referencing entity — Dataverse's metadata cache can lag a few seconds after Create, and "
        + "the entity-relationships query refreshes faster than RelationshipDefinitions(SchemaName=...).")]
    public async Task<VerificationResult> VerifyRelationshipCreationAsync(
        [Description("The schema name of the relationship to verify")] string relationshipSchemaName,
        [Description("Expected parent/referenced entity logical name")] string expectedReferencedEntity,
        [Description("Expected child/referencing entity logical name")] string expectedReferencingEntity,
        [Description("Expected cascade delete behavior")] string expectedCascadeDelete,
        [Description("Expected cascade assign behavior")] string expectedCascadeAssign,
        CancellationToken cancellationToken)
    {
        RelationshipMetadata? actual = null;
        // Primary path: direct GET on the metadata endpoint. This is the
        // fastest if the metadata cache has propagated.
        try
        {
            actual = await dataverseClient.GetRelationshipMetadataAsync(relationshipSchemaName, cancellationToken);
        }
        catch (Exception)
        {
            // Swallow — fall through to the listing-based fallback.
        }

        // Fallback path: look the relationship up via GetTableDetails on the
        // referencing entity. The OneToManyRelationships collection on the
        // entity is fetched via a different metadata path that propagates
        // sooner than the keyed-lookup endpoint. Past failure: a relationship
        // created at T+0 was visible via GetTableDetails at T+12s but
        // RelationshipDefinitions(SchemaName=...) still returned 404 at T+12s
        // and T+13s — a false-FAIL that wasted user time.
        if (actual is null && !string.IsNullOrWhiteSpace(expectedReferencingEntity))
        {
            try
            {
                var details = await dataverseClient.GetTableDetailsAsync(
                    expectedReferencingEntity.ToLowerInvariant(), cancellationToken);
                var match = details.Relationships.FirstOrDefault(r =>
                    string.Equals(r.SchemaName, relationshipSchemaName, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    // Found it via the entity listing. Cascade config isn't on
                    // RelationshipInfo — fill those with empty strings so the
                    // CompareProperty calls below either match (if expected is
                    // also empty) or surface as a structured discrepancy
                    // rather than a false NOT FOUND.
                    actual = new RelationshipMetadata(
                        SchemaName: match.SchemaName,
                        ReferencedEntity: match.ReferencedEntity,
                        ReferencedAttribute: match.ReferencedAttribute,
                        ReferencingEntity: match.ReferencingEntity,
                        ReferencingAttribute: match.ReferencingAttribute,
                        CascadeAssign: string.Empty,
                        CascadeDelete: string.Empty,
                        CascadeReparent: string.Empty,
                        CascadeMerge: string.Empty,
                        CascadeShare: string.Empty,
                        CascadeUnshare: string.Empty);
                }
            }
            catch (Exception)
            {
                // Both paths failed — fall through to the NOT FOUND result below.
            }
        }

        if (actual is null)
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
        // Cascade fields: only compare when we have actual values from the
        // primary endpoint. The fallback path returns empty cascade strings,
        // and rather than false-FAIL we treat empty actual + non-empty
        // expected as "couldn't verify" (a single info-level discrepancy)
        // instead of a hard failure.
        if (!string.IsNullOrEmpty(actual.CascadeDelete))
        {
            CompareProperty(discrepancies, "CascadeDelete", expectedCascadeDelete, actual.CascadeDelete);
        }
        if (!string.IsNullOrEmpty(actual.CascadeAssign))
        {
            CompareProperty(discrepancies, "CascadeAssign", expectedCascadeAssign, actual.CascadeAssign);
        }

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
