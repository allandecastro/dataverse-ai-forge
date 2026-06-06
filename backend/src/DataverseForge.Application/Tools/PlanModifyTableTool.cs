using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanModifyTableTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    // Editable table-level properties per MS Learn ("Customize table definitions").
    // Capability switches that can be ENABLED but NOT disabled (HasNotes, HasActivities,
    // IsBusinessProcessEnabled, IsConnectionsEnabled, IsActivityParty) are accepted with
    // a warning if the user tries to flip them off — Dataverse silently ignores the off
    // path, so the agent shouldn't expect that to work.
    private static readonly HashSet<string> AllowedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "displayName", "displayCollectionName", "description",
        "isQuickCreateEnabled", "isAuditEnabled",
        "isValidForQueue", "isConnectionsEnabled", "isMailMergeEnabled",
        "isDocumentManagementEnabled", "isDuplicateDetectionEnabled",
        "isAvailableOffline", "isVisibleInMobile", "isVisibleInMobileClient",
        "isReadOnlyInMobileClient", "autoCreateAccessTeams",
        "hasNotes", "hasActivities", "isBusinessProcessEnabled",
        "autoRouteToOwnerQueue", "primaryImageAttribute", "entitySetName",
    };

    private static readonly HashSet<string> EnableOnlyProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "hasNotes", "hasActivities", "isBusinessProcessEnabled",
    };

    /// <summary>
    /// Properties that are NOT valid on Activity tables (per MS Learn). The
    /// metadata API ignores them silently for activities, which leads to "I
    /// turned on Quick Create but it didn't appear" follow-ups. Reject up-front.
    /// </summary>
    private static readonly HashSet<string> ActivityInvalidProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "isQuickCreateEnabled",
    };

    [Description("Plan a modification to an existing Dataverse table's definition. Pass a JSON object "
        + "with the properties to change. Most useful for: enabling Quick Create on a table, toggling "
        + "audit/duplicate-detection/queues, exposing the table in mobile, renaming the display name. "
        + "Properties accepted: "
        + "displayName, displayCollectionName, description, primaryImageAttribute, entitySetName (string); "
        + "isQuickCreateEnabled, isAuditEnabled, isValidForQueue, isConnectionsEnabled, "
        + "isMailMergeEnabled, isDocumentManagementEnabled, isDuplicateDetectionEnabled, "
        + "isAvailableOffline, isVisibleInMobile, isVisibleInMobileClient, isReadOnlyInMobileClient, "
        + "autoCreateAccessTeams, autoRouteToOwnerQueue (boolean); "
        + "hasNotes, hasActivities, isBusinessProcessEnabled (boolean — ENABLE-only; Dataverse "
        + "won't let you turn these back off). "
        + "REJECTS: rename of an IsRenameable=false table; activity tables don't support "
        + "isQuickCreateEnabled. WARNS on entitySetName changes (breaks existing API consumers). "
        + "The PUT payload is stored server-side under planId; pass the planId verbatim to "
        + "execute_modify_table after approval.")]
    public async Task<TableModificationPlan> PlanModifyTableAsync(
        [Description("Logical name of the table to modify (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("JSON object of modifications, e.g. {\"isQuickCreateEnabled\":true,\"isAuditEnabled\":true} "
            + "or {\"displayName\":\"Gift Item\",\"isQuickCreateEnabled\":true}")] string modificationsJson,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var modifications = new List<FieldModification>();
        var t = tableLogicalName.ToLowerInvariant();

        var mods = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(modificationsJson) ?? [];

        // Verify the table exists first — surface a clean rejection rather than a
        // generic Dataverse 404 mid-execute.
        TableMetadata? tableMeta = null;
        try
        {
            tableMeta = await dataverseClient.GetTableMetadataAsync(t, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new TableModificationPlan(
                PlanId: $"rejected_modify_table_{Guid.NewGuid():N}",
                TableLogicalName: t,
                Modifications: modifications.AsReadOnly(),
                Warnings: new List<string>
                {
                    $"REJECTED: Table '{t}' does not exist in this environment. "
                    + "Custom tables typically have a publisher prefix (e.g. 'eppc_gift'). "
                    + $"Underlying lookup error: {ex.Message}"
                }.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        // Validate property names up-front so the agent gets every invalid key in one shot.
        foreach (var key in mods.Keys)
        {
            if (!AllowedProperties.Contains(key))
            {
                warnings.Add($"Property '{key}' cannot be modified at the table level. "
                    + $"Allowed: {string.Join(", ", AllowedProperties)}.");
            }
        }

        // Activity-table guards. Per MS Learn, activity tables don't expose
        // Quick Create — Dataverse silently ignores the toggle. Surface this
        // up-front rather than waiting for the user to wonder why nothing
        // changed.
        if (tableMeta.IsActivity)
        {
            foreach (var key in mods.Keys)
            {
                if (ActivityInvalidProperties.Contains(key))
                {
                    warnings.Add($"Activity table '{t}' does NOT support '{key}'. "
                        + "Dataverse will silently ignore this on activities. Drop the property "
                        + "from the modifications JSON or expect no behavior change.");
                }
            }
        }

        // IsRenameable check — a rename attempt against a managed-property-locked
        // table will be rejected by Dataverse with an opaque 400. Surface it as
        // a clean rejection.
        if (mods.ContainsKey("displayName") && !tableMeta.IsRenameable)
        {
            return new TableModificationPlan(
                PlanId: $"rejected_modify_table_{Guid.NewGuid():N}",
                TableLogicalName: t,
                Modifications: modifications.AsReadOnly(),
                Warnings: new List<string>
                {
                    $"REJECTED: Table '{t}' is locked by the IsRenameable=false managed property. "
                    + "Its display name cannot be changed through the Web API. This typically "
                    + "applies to OOB tables or managed-solution tables. Drop the displayName "
                    + "modification or work in the maker portal."
                }.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        // entitySetName warning — the OData entity-set (collection) name on a
        // table is exposed through the metadata API but changing it AFTER rows
        // exist breaks every existing Web API consumer that has hard-coded the
        // current name. Warn loudly.
        if (mods.ContainsKey("entitySetName"))
        {
            warnings.Add("CAUTION: 'entitySetName' is the OData collection name (e.g. 'eppc_gifts') "
                + "used by every Web API consumer of this table. Changing it AFTER any code or "
                + "Power Automate flow has been written against the table will break those consumers "
                + "with 404s. Only rename early — ideally while the table is empty.");
        }

        // Required base fields on a metadata PUT. SchemaName must match exactly —
        // omitting it makes Dataverse think you're trying to rename the table, which
        // it rejects.
        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.EntityMetadata",
            ["LogicalName"] = t,
            ["SchemaName"] = tableMeta.SchemaName,
        };

        // Plain string label fields → wrap in LocalizedLabel.
        ApplyLabel(mods, "displayName", "DisplayName", payload, modifications);
        ApplyLabel(mods, "displayCollectionName", "DisplayCollectionName", payload, modifications);
        ApplyLabel(mods, "description", "Description", payload, modifications);

        // Plain string fields (no LocalizedLabel wrapping).
        ApplyString(mods, "primaryImageAttribute", "PrimaryImageAttribute", payload, modifications);
        ApplyString(mods, "entitySetName", "EntitySetName", payload, modifications);

        // Plain boolean fields (NOT BooleanManagedProperty) per Web API metadata schema.
        ApplyBool(mods, "isQuickCreateEnabled", "IsQuickCreateEnabled", payload, modifications, warnings);
        ApplyBool(mods, "isAvailableOffline", "IsAvailableOffline", payload, modifications, warnings);
        ApplyBool(mods, "isDocumentManagementEnabled", "IsDocumentManagementEnabled", payload, modifications, warnings);
        ApplyBool(mods, "autoCreateAccessTeams", "AutoCreateAccessTeams", payload, modifications, warnings);
        ApplyBool(mods, "hasNotes", "HasNotes", payload, modifications, warnings);
        ApplyBool(mods, "hasActivities", "HasActivities", payload, modifications, warnings);
        ApplyBool(mods, "isBusinessProcessEnabled", "IsBusinessProcessEnabled", payload, modifications, warnings);
        ApplyBool(mods, "autoRouteToOwnerQueue", "AutoRouteToOwnerQueue", payload, modifications, warnings);

        // BooleanManagedProperty fields ({ Value, CanBeChanged }).
        ApplyBoolManaged(mods, "isAuditEnabled", "IsAuditEnabled", payload, modifications);
        ApplyBoolManaged(mods, "isValidForQueue", "IsValidForQueue", payload, modifications);
        ApplyBoolManaged(mods, "isConnectionsEnabled", "IsConnectionsEnabled", payload, modifications);
        ApplyBoolManaged(mods, "isMailMergeEnabled", "IsMailMergeEnabled", payload, modifications);
        ApplyBoolManaged(mods, "isDuplicateDetectionEnabled", "IsDuplicateDetectionEnabled", payload, modifications);
        ApplyBoolManaged(mods, "isVisibleInMobile", "IsVisibleInMobile", payload, modifications);
        ApplyBoolManaged(mods, "isVisibleInMobileClient", "IsVisibleInMobileClient", payload, modifications);
        ApplyBoolManaged(mods, "isReadOnlyInMobileClient", "IsReadOnlyInMobileClient", payload, modifications);

        if (modifications.Count == 0)
        {
            warnings.Add("No valid modifications specified.");
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"modify_table_{Guid.NewGuid():N}";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new TableModificationPlan(
            PlanId: planId,
            TableLogicalName: t,
            Modifications: modifications.AsReadOnly(),
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Yellow);
    }

    private static void ApplyLabel(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> modList)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        var v = el.GetString() ?? string.Empty;
        modList.Add(new FieldModification(odataKey, "(current)", v));
        payload[odataKey] = ToolHelpers.BuildLocalizedLabel(v);
    }

    private static void ApplyString(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> modList)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (el.ValueKind != JsonValueKind.String) return;
        var v = el.GetString() ?? string.Empty;
        modList.Add(new FieldModification(odataKey, "(current)", v));
        payload[odataKey] = v;
    }

    private static void ApplyBool(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> modList,
        List<string> warnings)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        var v = el.GetBoolean();
        modList.Add(new FieldModification(odataKey, "(current)", v.ToString()));
        payload[odataKey] = v;

        if (!v && EnableOnlyProperties.Contains(key))
        {
            warnings.Add($"'{key}' is ENABLE-only — Dataverse won't let you turn it back off "
                + "once enabled. The PUT will be accepted, but the platform will keep it on.");
        }
    }

    private static void ApplyBoolManaged(
        IDictionary<string, JsonElement> mods,
        string key, string odataKey,
        IDictionary<string, object> payload,
        List<FieldModification> modList)
    {
        if (!mods.TryGetValue(key, out var el)) return;
        if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return;
        var v = el.GetBoolean();
        modList.Add(new FieldModification(odataKey, "(current)", v.ToString()));
        payload[odataKey] = new Dictionary<string, object> { ["Value"] = v, ["CanBeChanged"] = true };
    }
}
