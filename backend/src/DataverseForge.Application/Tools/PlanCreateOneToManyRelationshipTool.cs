using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateOneToManyRelationshipTool(
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

        // Derive names
        var lookupBaseName = lookupFieldDisplayName.Replace(" ", "").ToLowerInvariant();
        var lookupFieldLogicalName = $"{publisherPrefix}_{lookupBaseName}id";
        var lookupFieldSchemaName = $"{publisherPrefix}_{lookupFieldDisplayName.Replace(" ", "")}Id";

        // Auto-generate relationship schema name if not provided
        var relationshipSchemaName = $"{publisherPrefix}_{primaryTable.Replace($"{publisherPrefix}_", "")}_{relatedTable.Replace($"{publisherPrefix}_", "")}";

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
