using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans a multi-table polymorphic lookup column. Wraps the
/// <c>CreatePolymorphicLookupAttribute</c> Web API action: the action atomically
/// creates ONE lookup column on the referencing table plus N×OneToMany
/// relationships, one per target table. The agent should NOT split this into
/// N separate plan_create_one_to_many_relationship calls — those produce N
/// independent single-target lookups, which is a different shape.
/// </summary>
public class PlanCreatePolymorphicLookupTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Plan a MULTI-TABLE polymorphic lookup column — one lookup that can point at rows "
        + "from any of several target tables. Use this when the user asks for 'a lookup to Book OR "
        + "Audio OR Video' / 'a Media reference' / similar polymorphic shapes. Wraps the Dataverse "
        + "CreatePolymorphicLookupAttribute action: atomically creates the lookup + N relationships "
        + "(one per target table). NOT the same as N separate single-target lookups. "
        + "If the targets are exactly Account+Contact, prefer plan_create_customer_lookup which uses "
        + "Dataverse's first-class Customer machinery. "
        + "Default cascade per relationship: Delete=RemoveLink, everything else=NoCascade. "
        + "Stores the OData JSON server-side under planId.")]
    public async Task<PolymorphicLookupCreationPlan> PlanCreatePolymorphicLookupAsync(
        [Description("Logical name of the table that will OWN the lookup column (the 'referencing' / 'many' side, e.g. 'sample_media')")] string referencingTable,
        [Description("Comma-separated list of target table logical names — at least 2 (e.g. 'sample_book, sample_audio, sample_video'). For exactly Account+Contact, use plan_create_customer_lookup instead.")] string referencedTables,
        [Description("Display name for the lookup column (e.g., 'Media Reference')")] string lookupDisplayName,
        [Description("Publisher customization prefix used for the lookup schema name + relationship schema names (e.g. 'sample')")] string publisherPrefix,
        [Description("Description for the lookup column. Pass empty string for none.")] string description,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var t = (referencingTable ?? string.Empty).Trim().ToLowerInvariant();
        var prefix = (publisherPrefix ?? string.Empty).Trim().ToLowerInvariant();

        var targets = (referencedTables ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (targets.Count < 2)
        {
            return Reject(t, targets, lookupDisplayName, description,
                "REJECTED: A polymorphic lookup needs AT LEAST 2 target tables. "
                + "Got " + targets.Count + ". For a single target, use plan_create_one_to_many_relationship.");
        }

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Reject(t, targets, lookupDisplayName, description,
                "REJECTED: publisherPrefix is required (e.g. 'sample'). Used to build "
                + "the lookup schema name and per-target relationship schema names.");
        }

        if (string.IsNullOrWhiteSpace(lookupDisplayName))
        {
            return Reject(t, targets, lookupDisplayName, description,
                "REJECTED: lookupDisplayName is required.");
        }

        var account = targets.Any(x => x.Equals("account", StringComparison.OrdinalIgnoreCase));
        var contact = targets.Any(x => x.Equals("contact", StringComparison.OrdinalIgnoreCase));
        if (targets.Count == 2 && account && contact)
        {
            warnings.Add("Targets are exactly Account+Contact — prefer plan_create_customer_lookup. "
                + "It uses Dataverse's first-class Customer column type so the maker portal shows the "
                + "field as 'Customer' rather than a generic polymorphic Lookup. This plan will still "
                + "work but the column will be type 'Lookup'.");
        }

        // Verify the referencing table exists; surface a clean rejection rather
        // than a generic 400 mid-execute.
        try
        {
            _ = await dataverseClient.GetTableMetadataAsync(t, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Reject(t, targets, lookupDisplayName, description,
                $"REJECTED: Referencing table '{t}' does not exist. Underlying error: {ex.Message}");
        }

        // Verify each target table exists. Skipping this check is what produced
        // multi-minute thrashing on 1:N earlier — better to fail at plan time.
        var missingTargets = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                _ = await dataverseClient.GetTableMetadataAsync(target, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                missingTargets.Add($"{target} ({ex.Message})");
            }
        }
        if (missingTargets.Count > 0)
        {
            return Reject(t, targets, lookupDisplayName, description,
                "REJECTED: Target table(s) do not exist: " + string.Join("; ", missingTargets));
        }

        // Schema names — Dataverse expects PascalCase on the lookup SchemaName.
        var lookupBase = lookupDisplayName.Replace(" ", string.Empty);
        var lookupSchemaName = $"{prefix}_{lookupBase}";

        // Per-target relationship schema. Dataverse's own examples use
        // "{referencing}_{referenced}" — we keep the publisher prefix on each
        // for solution-component consistency.
        string Strip(string logical) => logical.StartsWith(prefix + "_", StringComparison.Ordinal)
            ? logical.Substring(prefix.Length + 1)
            : logical;

        var relSchemas = targets
            .Select(tgt => $"{prefix}_{Strip(t)}_{Strip(tgt)}")
            .ToList();

        // Build OneToManyRelationships array. Per the official sample, Dataverse
        // accepts a default Cascade block when omitted — we emit it explicitly so
        // the payload is self-documenting and the user sees what they're getting.
        var oneToManyRelationships = new List<Dictionary<string, object>>(targets.Count);
        for (var i = 0; i < targets.Count; i++)
        {
            oneToManyRelationships.Add(new Dictionary<string, object>
            {
                ["ReferencedEntity"] = targets[i],
                ["SchemaName"] = relSchemas[i],
                ["ReferencingEntity"] = t,
                ["CascadeConfiguration"] = new Dictionary<string, object>
                {
                    ["Assign"] = "NoCascade",
                    ["Share"] = "NoCascade",
                    ["Delete"] = "RemoveLink",
                    ["Merge"] = "NoCascade",
                    ["Reparent"] = "NoCascade",
                    ["Unshare"] = "NoCascade",
                }
            });
        }

        var lookup = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.ComplexLookupAttributeMetadata",
            ["AttributeType"] = "Lookup",
            ["AttributeTypeName"] = new Dictionary<string, object> { ["Value"] = "LookupType" },
            ["SchemaName"] = lookupSchemaName,
            ["DisplayName"] = ToolHelpers.BuildLocalizedLabel(lookupDisplayName),
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            lookup["Description"] = ToolHelpers.BuildLocalizedLabel(description);
        }

        var payload = new Dictionary<string, object>
        {
            ["OneToManyRelationships"] = oneToManyRelationships,
            ["Lookup"] = lookup,
        };

        // Pull the active solution from project state so Dataverse adds the
        // lookup + relationships to the user's solution atomically.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(state.SolutionUniqueName))
        {
            payload["SolutionUniqueName"] = state.SolutionUniqueName;
        }
        else
        {
            warnings.Add("No active solution selected — the polymorphic lookup will be created in the "
                + "default solution. Call select_solution before execute if you want it in a specific solution.");
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"create_polymorphic_lookup_{Guid.NewGuid():N}";
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new PolymorphicLookupCreationPlan(
            PlanId: planId,
            ReferencingTable: t,
            ReferencedTables: targets.AsReadOnly(),
            LookupSchemaName: lookupSchemaName,
            LookupDisplayName: lookupDisplayName,
            Description: description ?? string.Empty,
            RelationshipSchemaNames: relSchemas.AsReadOnly(),
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Yellow);
    }

    private static PolymorphicLookupCreationPlan Reject(
        string referencingTable,
        IReadOnlyList<string> targets,
        string displayName,
        string description,
        string warning)
    {
        return new PolymorphicLookupCreationPlan(
            PlanId: $"rejected_polymorphic_lookup_{Guid.NewGuid():N}",
            ReferencingTable: referencingTable,
            ReferencedTables: targets,
            LookupSchemaName: string.Empty,
            LookupDisplayName: displayName ?? string.Empty,
            Description: description ?? string.Empty,
            RelationshipSchemaNames: Array.Empty<string>(),
            Warnings: new List<string> { warning }.AsReadOnly(),
            RiskLevel: PlanRisk.Red);
    }
}
