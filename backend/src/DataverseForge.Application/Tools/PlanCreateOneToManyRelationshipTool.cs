using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateOneToManyRelationshipTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for creating a 1:N relationship between two Dataverse tables. "
        + "Returns a planId, ERD diagram fragment, validation warnings, and risk level. The full OData "
        + "JSON payload is stored server-side under planId so it cannot be mangled. Pass the planId "
        + "verbatim to execute_create_one_to_many_relationship after approval. "
        + "Pick lookupBehavior carefully: 'Referential' is the SAFE DEFAULT for most lookups (no cascade). "
        + "'Parental' should be RARE — each child table can have at most ONE parental relationship in the "
        + "whole environment (Dataverse rejects a second parent), and parental cascades delete + reassign + "
        + "share + unshare from parent to child. When lookupBehavior is one of the 3 presets the cascade* "
        + "params are IGNORED and derived from the preset; pass empty strings for them.")]
    public async Task<RelationshipCreationPlan> PlanCreateOneToManyRelationshipAsync(
        [Description("Logical name of the primary (one-side / parent) table (e.g., 'cr123_project')")] string primaryTable,
        [Description("Logical name of the related (many-side / child) table (e.g., 'cr123_task')")] string relatedTable,
        [Description("Display name for the lookup field on the related table (e.g., 'Project')")] string lookupFieldDisplayName,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        [Description("Lookup behavior preset (the PRIMARY control). 'Referential' = no cascading, child stays when parent is deleted (use this 90% of the time). 'ReferentialRestrict' = no cascading + parent cannot be deleted while children exist. 'Parental' = parent cascades everything to children (assign/delete/share/unshare/reparent) — only ONE parental relationship allowed per child table. 'Configurable' = use the cascade* params explicitly.")] string lookupBehavior,
        [Description("ONLY used when lookupBehavior='Configurable'. Cascade behavior for Assign: 'Cascade', 'Active', 'UserOwned', 'NoCascade'. Pass empty string when using a preset.")] string cascadeAssign,
        [Description("ONLY used when lookupBehavior='Configurable'. Cascade behavior for Delete: 'Cascade', 'RemoveLink', 'Restrict'. Pass empty string when using a preset.")] string cascadeDelete,
        [Description("ONLY used when lookupBehavior='Configurable'. Cascade behavior for Reparent: 'Cascade', 'Active', 'UserOwned', 'NoCascade'. Pass empty string when using a preset.")] string cascadeReparent,
        [Description("ONLY used when lookupBehavior='Configurable'. Cascade behavior for Merge: 'Cascade', 'NoCascade'. Pass empty string when using a preset.")] string cascadeMerge,
        [Description("ONLY used when lookupBehavior='Configurable'. Cascade behavior for Share: 'Cascade', 'Active', 'UserOwned', 'NoCascade'. Pass empty string when using a preset.")] string cascadeShare,
        [Description("ONLY used when lookupBehavior='Configurable'. Cascade behavior for Unshare: 'Cascade', 'Active', 'UserOwned', 'NoCascade'. Pass empty string when using a preset.")] string cascadeUnshare,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Normalize the publisher prefix — Dataverse logical names must be
        // lowercase. Same fix as PlanCreateFieldTool / PlanCreateTableTool.
        // CLAUDE.md #3.
        var normalizedPrefix = (publisherPrefix ?? string.Empty).Trim().ToLowerInvariant();
        if (!string.Equals(normalizedPrefix, publisherPrefix, StringComparison.Ordinal))
        {
            warnings.Add($"publisherPrefix '{publisherPrefix}' was normalized to '{normalizedPrefix}'.");
            publisherPrefix = normalizedPrefix;
        }
        // Lowercase the table logical names too — agents pass mixed case
        // occasionally and the case-sensitive Replace below would otherwise
        // fail to strip the prefix from a "EPPC_Foo" → "_Foo" remainder.
        primaryTable = (primaryTable ?? string.Empty).Trim().ToLowerInvariant();
        relatedTable = (relatedTable ?? string.Empty).Trim().ToLowerInvariant();

        // Pre-existence checks — same shape as PlanCreateField. Dataverse
        // 400s mid-execute when a referenced table doesn't exist; better to
        // fail at plan time with a clear "primary table not found" message.
        if (string.IsNullOrWhiteSpace(primaryTable) || string.IsNullOrWhiteSpace(relatedTable))
        {
            return RejectedPlan(primaryTable, relatedTable, lookupFieldDisplayName, warnings,
                "primaryTable and relatedTable are required.");
        }
        // GetTableMetadataAsync throws HttpRequestException on 404. Treat the
        // throw as authoritative "table doesn't exist" and return a hard
        // rejection — matches the spirit of CLAUDE.md "fail at plan time, not
        // mid-execute". Other exceptions (auth, transient, etc.) become a
        // soft warning so a flaky network doesn't block legitimate plans.
        try
        {
            await dataverseClient.GetTableMetadataAsync(primaryTable, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return RejectedPlan(primaryTable, relatedTable, lookupFieldDisplayName, warnings,
                $"Primary table '{primaryTable}' does not exist in this environment. Pass the exact logical name (e.g. 'account', 'eppc_gift').");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Could not pre-validate primary table '{primaryTable}' ({ex.Message}); proceeding — execute may fail with a clearer error.");
        }
        try
        {
            await dataverseClient.GetTableMetadataAsync(relatedTable, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return RejectedPlan(primaryTable, relatedTable, lookupFieldDisplayName, warnings,
                $"Related (child) table '{relatedTable}' does not exist in this environment.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Could not pre-validate related table '{relatedTable}' ({ex.Message}); proceeding.");
        }

        // Derive cascade config from the preset when one is given. Past failure:
        // agent passed cascadeAssign='Cascade' / cascadeDelete='Cascade' / etc.
        // because the LLM defaulted to "all cascades on" without realizing that
        // produces a Parental relationship. Then a second Parental relationship
        // attempt failed with "Entity is already parented" because Dataverse
        // allows only ONE parental relationship per child entity.
        var preset = (lookupBehavior ?? string.Empty).Trim();
        var (assign, delete, reparent, merge, share, unshare) = preset.ToLowerInvariant() switch
        {
            "" or "referential" =>
                ("NoCascade", "RemoveLink", "NoCascade", "NoCascade", "NoCascade", "NoCascade"),
            "referentialrestrict" or "referential_restrict_delete" =>
                ("NoCascade", "Restrict", "NoCascade", "NoCascade", "NoCascade", "NoCascade"),
            "parental" =>
                ("Cascade", "Cascade", "Cascade", "Cascade", "Cascade", "Cascade"),
            "configurable" => (
                Default(cascadeAssign, "NoCascade"),
                Default(cascadeDelete, "RemoveLink"),
                Default(cascadeReparent, "NoCascade"),
                Default(cascadeMerge, "NoCascade"),
                Default(cascadeShare, "NoCascade"),
                Default(cascadeUnshare, "NoCascade")),
            _ => (
                "NoCascade", "RemoveLink", "NoCascade", "NoCascade", "NoCascade", "NoCascade"),
        };

        if (string.IsNullOrWhiteSpace(preset))
            preset = "Referential";

        // Per-action cascade validation. The CascadeType enum has 6 values:
        // NoCascade, Cascade, Active, UserOwned, RemoveLink, Restrict — but
        // each cascade slot accepts a DIFFERENT subset:
        //   Assign / Reparent / Share / Unshare → NoCascade, Cascade, Active, UserOwned
        //   Delete                              → NoCascade, Cascade, RemoveLink, Restrict
        //   Merge                               → NoCascade, Cascade only
        // Sending an invalid combo (e.g. Assign=Restrict) returns 400 from the
        // platform with no useful body. Validate up-front so the agent gets a
        // clean rejection with the actual rule.
        if (preset.Equals("Configurable", StringComparison.OrdinalIgnoreCase))
        {
            ValidateCascade(assign, "Assign", AssignReparentShareUnshare, warnings);
            ValidateCascade(delete, "Delete", DeleteValid, warnings);
            ValidateCascade(reparent, "Reparent", AssignReparentShareUnshare, warnings);
            ValidateCascade(merge, "Merge", MergeValid, warnings);
            ValidateCascade(share, "Share", AssignReparentShareUnshare, warnings);
            ValidateCascade(unshare, "Unshare", AssignReparentShareUnshare, warnings);
        }

        // Derive names. Sanitize the display name first so punctuation
        // (parens, hyphens, accents — "Account-Owner", "Owner (Primary)")
        // doesn't leak into the logical/schema name and trip Dataverse's
        // alpha-numeric-only rule. Same helper PlanCreateField uses.
        var sanitizedDisplay = ToolHelpers.SanitizeSchemaName(lookupFieldDisplayName);
        if (string.IsNullOrWhiteSpace(sanitizedDisplay))
        {
            return RejectedPlan(primaryTable, relatedTable, lookupFieldDisplayName, warnings,
                $"lookupFieldDisplayName '{lookupFieldDisplayName}' produces an empty schema name after sanitization. Use a name that contains at least one alphanumeric character.");
        }
        var lookupBaseName = sanitizedDisplay.ToLowerInvariant();
        var lookupFieldLogicalName = $"{publisherPrefix}_{lookupBaseName}id";
        var lookupFieldSchemaName = $"{publisherPrefix}_{sanitizedDisplay}Id";

        // Auto-generate relationship schema name. Strip the prefix from each
        // table name only if it's actually present — case-insensitive so a
        // mixed-case prefix passed through earlier mistakes (now lowercased
        // above) still produces a clean schema name.
        var primaryStripped = StripPrefix(primaryTable, publisherPrefix);
        var relatedStripped = StripPrefix(relatedTable, publisherPrefix);
        var relationshipSchemaName = $"{publisherPrefix}_{primaryStripped}_{relatedStripped}";

        // Surface the consequences of the chosen preset.
        if (delete.Equals("Cascade", StringComparison.OrdinalIgnoreCase))
            warnings.Add("Cascade delete will permanently remove all related records when the parent is deleted. Verify this is intended.");

        if (preset.Equals("Parental", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "Parental relationship: every child record inherits ownership/sharing changes from its "
                + $"parent and is deleted with it. Dataverse allows only ONE parental relationship per "
                + $"child entity, so '{relatedTable}' can have at most one parent — if it already has "
                + "one, this plan will fail at execute time. Most lookups should be 'Referential' instead.");
        }

        // Re-bind for downstream payload assembly
        var cascAssign = assign;
        var cascDelete = delete;
        var cascReparent = reparent;
        var cascMerge = merge;
        var cascShare = share;
        var cascUnshare = unshare;

        // Build OData JSON payload
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata",
            ["SchemaName"] = relationshipSchemaName,
            ["ReferencedEntity"] = primaryTable,
            ["ReferencingEntity"] = relatedTable,
            ["Lookup"] = new Dictionary<string, object>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.LookupAttributeMetadata",
                ["SchemaName"] = lookupFieldSchemaName,
                ["DisplayName"] = ToolHelpers.BuildLocalizedLabel(lookupFieldDisplayName),
                ["RequiredLevel"] = new Dictionary<string, object> { ["Value"] = "None", ["CanBeChanged"] = true }
            },
            ["CascadeConfiguration"] = new Dictionary<string, object>
            {
                ["Assign"] = cascAssign,
                ["Delete"] = cascDelete,
                ["Reparent"] = cascReparent,
                ["Merge"] = cascMerge,
                ["Share"] = cascShare,
                ["Unshare"] = cascUnshare
            },
            ["AssociatedMenuConfiguration"] = new Dictionary<string, object>
            {
                ["Behavior"] = "UseCollectionName",
                ["Group"] = "Details",
                ["Order"] = 10000
            }
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var planId = $"create_one_to_many_relationship_{Guid.NewGuid():N}";

        // Persist payload server-side so the agent never has to pass OData JSON
        // back. Past failure: agent rebuilt the payload with invented properties
        // ("LookupFieldDisplayName" doesn't exist on OneToManyRelationshipMetadata)
        // and Dataverse 400'd. The executor now looks up by planId.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        // Generate ERD fragment (Mermaid erDiagram syntax)
        var erdFragment = $"{primaryTable} ||--o{{ {relatedTable} : \"{relationshipSchemaName}\"";

        return new RelationshipCreationPlan(
            PlanId: planId,
            PrimaryTable: primaryTable,
            RelatedTable: relatedTable,
            LookupFieldLogicalName: lookupFieldLogicalName,
            LookupFieldDisplayName: lookupFieldDisplayName,
            RelationshipSchemaName: relationshipSchemaName,
            CascadeAssign: cascAssign,
            CascadeDelete: cascDelete,
            CascadeReparent: cascReparent,
            CascadeMerge: cascMerge,
            CascadeShare: cascShare,
            CascadeUnshare: cascUnshare,
            LookupBehavior: preset,
            WebApiPayloadJson: payloadJson,
            ErDiagramFragment: erdFragment,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange");
    }

    private static string Default(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>
    /// Returns a rejected plan with empty PlanId — the executor / agent
    /// reads PlanId.IsNullOrEmpty as "rejected, do not execute" and the
    /// rejection reason rides in the Warnings list (no separate Reason
    /// field on RelationshipCreationPlan).
    /// </summary>
    private static RelationshipCreationPlan RejectedPlan(
        string primaryTable, string relatedTable, string lookupFieldDisplayName,
        List<string> warnings, string reason)
    {
        warnings.Insert(0, $"REJECTED: {reason}");
        return new RelationshipCreationPlan(
            PlanId: string.Empty,
            PrimaryTable: primaryTable ?? string.Empty,
            RelatedTable: relatedTable ?? string.Empty,
            LookupFieldLogicalName: string.Empty,
            LookupFieldDisplayName: lookupFieldDisplayName ?? string.Empty,
            RelationshipSchemaName: string.Empty,
            CascadeAssign: string.Empty, CascadeDelete: string.Empty, CascadeReparent: string.Empty,
            CascadeMerge: string.Empty, CascadeShare: string.Empty, CascadeUnshare: string.Empty,
            LookupBehavior: string.Empty,
            WebApiPayloadJson: string.Empty,
            ErDiagramFragment: string.Empty,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "rejected");
    }

    /// <summary>
    /// Case-insensitive `prefix_` strip. We've already lowercased everything
    /// above, but past versions of the table name may still slip through
    /// from agent messages — be defensive.
    /// </summary>
    private static string StripPrefix(string value, string? prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return value;
        var marker = $"{prefix}_";
        return value.StartsWith(marker, StringComparison.OrdinalIgnoreCase)
            ? value[marker.Length..]
            : value;
    }

    // Per the CascadeType enum doc, RemoveLink and Restrict ONLY apply to the
    // Delete slot — sending them on Assign/Reparent/Share/Unshare returns 400.
    private static readonly HashSet<string> AssignReparentShareUnshare =
        new(StringComparer.OrdinalIgnoreCase) { "NoCascade", "Cascade", "Active", "UserOwned" };

    private static readonly HashSet<string> DeleteValid =
        new(StringComparer.OrdinalIgnoreCase) { "NoCascade", "Cascade", "RemoveLink", "Restrict" };

    private static readonly HashSet<string> MergeValid =
        new(StringComparer.OrdinalIgnoreCase) { "NoCascade", "Cascade" };

    private static void ValidateCascade(
        string value, string slot, HashSet<string> allowed, List<string> warnings)
    {
        if (!allowed.Contains(value))
        {
            warnings.Add(
                $"Cascade slot '{slot}' got invalid value '{value}'. "
                + $"Allowed for {slot}: {string.Join(", ", allowed)}. "
                + "Per the CascadeType enum doc: RemoveLink/Restrict only apply to Delete; "
                + "Merge accepts only NoCascade or Cascade. Pick a valid value or use one "
                + "of the presets (Referential, ReferentialRestrict, Parental).");
        }
    }
}
