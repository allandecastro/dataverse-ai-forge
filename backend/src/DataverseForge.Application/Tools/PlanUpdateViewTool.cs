using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plan an update to an existing Dataverse system view (savedquery). Same
/// FetchXML/LayoutXML generators as the create tool; targets an existing
/// viewId via PATCH savedqueries({id}). Stores the OData payload server-side
/// under a planId to prevent agent payload mangling.
/// </summary>
public class PlanUpdateViewTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    XsdValidator xsdValidator)
{
    [Description("Generate a plan for UPDATING an existing Dataverse system view. Returns a planId, "
        + "FetchXML/LayoutXML previews, warnings, and risk level. The full OData JSON payload is stored "
        + "server-side under planId. Pass the planId verbatim to execute_update_view after approval. "
        + "Use get_views_list to find the viewId of the view to update. Validates that every column "
        + "actually exists on the table.")]
    public async Task<ViewUpdatePlan> PlanUpdateViewAsync(
        [Description("The GUID of the existing view to update (savedqueryid from get_views_list)")] string viewId,
        [Description("The logical name of the table the view belongs to (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("New display name for the view. Pass the existing name if you don't want to rename.")] string viewName,
        [Description("Description for the view")] string description,
        [Description("Comma-separated list of column LOGICAL names in display order (lowercase, e.g. 'eppc_giftname,eppc_type,eppc_contactid,createdon'). For lookups, use the lookup field (e.g. 'eppc_contactid').")] string columns,
        [Description("The field logical name to sort by (e.g., 'createdon')")] string sortField,
        [Description("Sort direction: 'ascending' or 'descending'")] string sortDirection,
        [Description("FetchXML filter condition (e.g., '<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />'), empty for no filter")] string filterCondition,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(viewId, out var parsedViewId) || parsedViewId == Guid.Empty)
        {
            return new ViewUpdatePlan(
                PlanId: $"rejected_view_invalid_{Guid.NewGuid():N}",
                ViewId: Guid.Empty,
                TableLogicalName: tableLogicalName,
                ViewName: viewName,
                Description: description,
                FetchXml: string.Empty,
                LayoutXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: new List<string> { "REJECTED: viewId must be a valid GUID from get_views_list." }.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        var warnings = new List<string>();
        var lowerTable = tableLogicalName.ToLowerInvariant();
        var columnList = PlanCreateViewTool.NormalizeFieldListPublic(columns);
        var lowerSortField = sortField?.ToLowerInvariant() ?? string.Empty;

        if (columnList.Count > 7)
            warnings.Add($"Views should have 5-7 columns max to avoid horizontal scroll. Current count: {columnList.Count}.");

        if (columnList.Count == 0)
            warnings.Add("View has no columns defined.");

        var primaryIdAttribute = $"{lowerTable}id";
        var primaryNameAttribute = "name";
        var columnTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var details = await dataverseClient.GetTableDetailsAsync(lowerTable, cancellationToken);
            if (!string.IsNullOrWhiteSpace(details.PrimaryIdAttribute))
                primaryIdAttribute = details.PrimaryIdAttribute;
            if (!string.IsNullOrWhiteSpace(details.PrimaryNameAttribute))
                primaryNameAttribute = details.PrimaryNameAttribute;
            foreach (var c in details.Columns)
            {
                columnTypes[c.LogicalName.ToLowerInvariant()] = c.AttributeType ?? string.Empty;
            }
            var available = new HashSet<string>(columnTypes.Keys, StringComparer.Ordinal);

            var allReferenced = new List<string>(columnList);
            if (!string.IsNullOrWhiteSpace(lowerSortField)) allReferenced.Add(lowerSortField);

            var missing = allReferenced
                .Where(f => !available.Contains(f) && !PlanCreateViewTool.IsKnownSystemFieldPublic(f))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (missing.Count > 0)
            {
                var sample = string.Join(", ", available.OrderBy(s => s).Take(15));
                var rejection = new List<string>
                {
                    $"REJECTED: The following column(s) do NOT exist on table '{lowerTable}': "
                    + string.Join(", ", missing) + ". "
                    + "View update will fail at Dataverse with HTTP 400. "
                    + "Available logical names on this table (sample): " + sample + "."
                };
                return new ViewUpdatePlan(
                    PlanId: $"rejected_view_invalid_{Guid.NewGuid():N}",
                    ViewId: parsedViewId,
                    TableLogicalName: lowerTable,
                    ViewName: viewName,
                    Description: description,
                    FetchXml: string.Empty,
                    LayoutXml: string.Empty,
                    WebApiPayloadJson: string.Empty,
                    Warnings: rejection.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var rejection = new List<string>
            {
                $"REJECTED: Table '{lowerTable}' does not exist in this environment. "
                + $"Update will fail. Underlying lookup error: {ex.Message}"
            };
            return new ViewUpdatePlan(
                PlanId: $"rejected_view_invalid_{Guid.NewGuid():N}",
                ViewId: parsedViewId,
                TableLogicalName: lowerTable,
                ViewName: viewName,
                Description: description,
                FetchXml: string.Empty,
                LayoutXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: rejection.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        var descending = sortDirection.Equals("descending", StringComparison.OrdinalIgnoreCase);

        var fetchXml = PlanCreateViewTool.BuildFetchXmlPublic(lowerTable, columnList, lowerSortField, descending, filterCondition);
        var layoutXml = PlanCreateViewTool.BuildLayoutXmlPublic(columnList, primaryIdAttribute, primaryNameAttribute, columnTypes);

        var xsdResult = xsdValidator.Validate(fetchXml, "Fetch.xsd");
        if (!xsdResult.IsValid)
        {
            return new ViewUpdatePlan(
                PlanId: $"rejected_view_invalid_{Guid.NewGuid():N}",
                ViewId: parsedViewId,
                TableLogicalName: lowerTable,
                ViewName: viewName,
                Description: description,
                FetchXml: fetchXml,
                LayoutXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: new List<string>
                {
                    "REJECTED: Generated FetchXML failed XSD validation: "
                    + string.Join("; ", xsdResult.Errors)
                }.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        // PATCH payload — never include `returnedtypecode` (immutable on existing
        // savedquery records). `name`/`description`/`fetchxml`/`layoutxml` are
        // mergeable and trigger Dataverse to recompute the view.
        var payload = new Dictionary<string, object>
        {
            ["name"] = viewName,
            ["description"] = description,
            ["fetchxml"] = fetchXml,
            ["layoutxml"] = layoutXml
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"update_view_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new ViewUpdatePlan(
            PlanId: planId,
            ViewId: parsedViewId,
            TableLogicalName: lowerTable,
            ViewName: viewName,
            Description: description,
            FetchXml: fetchXml,
            LayoutXml: layoutXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Orange);
    }
}
