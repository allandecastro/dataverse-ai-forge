using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateViewTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for creating a Dataverse system view. Returns a planId, FetchXML/LayoutXML "
        + "previews, warnings, and risk level. The full OData JSON payload is stored server-side under planId "
        + "so it cannot be mangled. Pass the planId verbatim to execute_create_view after approval. Validates "
        + "that every column actually exists on the table — call get_table_details first if you're unsure of logical names.")]
    public async Task<ViewCreationPlan> PlanCreateViewAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("The display name for the view (e.g., 'Active Work Orders')")] string viewName,
        [Description("Description of the view")] string description,
        [Description("Comma-separated list of column LOGICAL names. Use lowercase logical names (e.g., 'eppc_giftname,eppc_type'), NOT display names. For lookups, use the lookup field (e.g., 'eppc_contactid'), not the related entity name.")] string columns,
        [Description("The field logical name to sort by (e.g., 'createdon')")] string sortField,
        [Description("Sort direction: 'ascending' or 'descending'")] string sortDirection,
        [Description("FetchXML filter condition (e.g., '<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />'), empty for no filter")] string filterCondition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var columnList = NormalizeFieldList(columns);
        var lowerSortField = sortField?.ToLowerInvariant() ?? string.Empty;
        var lowerTable = tableLogicalName.ToLowerInvariant();

        // Validate column count
        if (columnList.Count > 7)
            warnings.Add($"Views should have 5-7 columns max to avoid horizontal scroll. Current count: {columnList.Count}.");

        if (columnList.Count == 0)
            warnings.Add("View has no columns defined.");

        // Captured outside the try so BuildLayoutXml can use the real values per
        // the MS FormXML/LayoutXml schema: <row id="..."> = primary id attribute,
        // <grid jump="..."> = primary name attribute.
        var primaryIdAttribute = $"{lowerTable}id"; // safe convention fallback
        var primaryNameAttribute = "name";          // safe convention fallback
        var columnTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        // Validate every referenced column exists on the table. Past failure mode:
        // agent passed eppc_contactid for a missing lookup; Dataverse rejected the
        // savedquery payload with HTTP 400 and the agent looped on retries with no
        // signal what was wrong. Surface a rejection plan instead.
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
            var available = new HashSet<string>(
                columnTypes.Keys,
                StringComparer.Ordinal);

            // Sort field can be a system field like createdon — only flag if it's clearly
            // intended as a custom field (has a prefix matching the table) and missing.
            var allReferenced = new List<string>(columnList);
            if (!string.IsNullOrWhiteSpace(lowerSortField)) allReferenced.Add(lowerSortField);

            var missing = allReferenced
                .Where(f => !available.Contains(f) && !IsKnownSystemField(f))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (missing.Count > 0)
            {
                var sample = string.Join(", ", available.OrderBy(s => s).Take(15));
                var rejection = new List<string>
                {
                    $"REJECTED: The following column(s) do NOT exist on table '{lowerTable}': "
                    + string.Join(", ", missing) + ". "
                    + "View creation will fail at Dataverse with HTTP 400. "
                    + "Available logical names on this table (sample): " + sample + ". "
                    + "Either fix the spelling (logical names are lowercase), drop the missing columns, "
                    + "or call plan_create_field / plan_create_one_to_many_relationship FIRST to create them."
                };
                return new ViewCreationPlan(
                    PlanId: $"rejected_view_invalid_{Guid.NewGuid():N}",
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
            // Hard-reject when the table itself doesn't exist. Same pattern as
            // PlanCreateForm — agent passes "gift" instead of "eppc_gift" and the
            // executor would otherwise send a doomed payload to Dataverse.
            var rejection = new List<string>
            {
                $"REJECTED: Table '{lowerTable}' does not exist in this environment. "
                + "View creation will fail. Custom tables typically have a publisher prefix "
                + "(e.g. 'eppc_gift', not 'gift'). Call get_tables_list(filter:'solution', solutionName:<active>) "
                + $"to confirm the correct logical name. Underlying lookup error: {ex.Message}"
            };
            return new ViewCreationPlan(
                PlanId: $"rejected_view_invalid_{Guid.NewGuid():N}",
                TableLogicalName: lowerTable,
                ViewName: viewName,
                Description: description,
                FetchXml: string.Empty,
                LayoutXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: rejection.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        // Validate sort direction
        var descending = sortDirection.Equals("descending", StringComparison.OrdinalIgnoreCase);

        // Build FetchXML
        var fetchXml = BuildFetchXml(lowerTable, columnList, lowerSortField, descending, filterCondition);

        // Build LayoutXML — pass real PrimaryIdAttribute / PrimaryNameAttribute
        // per the MS spec. <row id="..."> must be the primary id attribute name
        // (e.g. "eppc_giftid"), and <grid jump="..."> must be the primary name
        // attribute (e.g. "eppc_giftname"). Past failure: hardcoded GUID id and
        // jump="name" → broken navigation on custom tables.
        var layoutXml = BuildLayoutXml(columnList, primaryIdAttribute, primaryNameAttribute, columnTypes);

        // Build OData payload
        var payload = new Dictionary<string, object>
        {
            ["returnedtypecode"] = lowerTable,
            ["name"] = viewName,
            ["description"] = description,
            ["querytype"] = 0, // Public view
            ["fetchxml"] = fetchXml,
            ["layoutxml"] = layoutXml
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        // planId-by-reference. Same rationale as PlanCreateForm: the payload
        // includes embedded FetchXML and LayoutXML that the agent has historically
        // mangled when asked to pass it through Execute, producing "Stream was
        // not readable" 400s.
        var planId = $"create_view_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new ViewCreationPlan(
            PlanId: planId,
            TableLogicalName: lowerTable,
            ViewName: viewName,
            Description: description,
            FetchXml: fetchXml,
            LayoutXml: layoutXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Green);
    }

    /// <summary>
    /// Allow PlanUpdateViewTool (same assembly) to reuse the FetchXML / LayoutXML
    /// generators. Same shape works for both create (POST savedqueries) and
    /// update (PATCH savedqueries({id})).
    /// </summary>
    internal static string BuildFetchXmlPublic(
        string tableLogicalName, List<string> columns, string sortField, bool descending, string filterCondition)
        => BuildFetchXml(tableLogicalName, columns, sortField, descending, filterCondition);

    internal static string BuildLayoutXmlPublic(
        List<string> columns, string primaryIdAttribute, string primaryNameAttribute,
        IReadOnlyDictionary<string, string> columnTypes)
        => BuildLayoutXml(columns, primaryIdAttribute, primaryNameAttribute, columnTypes);

    internal static bool IsKnownSystemFieldPublic(string name) => IsKnownSystemField(name);

    internal static List<string> NormalizeFieldListPublic(string fields) => NormalizeFieldList(fields);

    /// <summary>
    /// System / framework fields that are valid in fetchxml on every entity but
    /// don't appear in EntityDefinitions(...).Attributes via GetTableDetails (or
    /// they appear under different casing). We allow them through so the validator
    /// doesn't false-positive on common sort columns.
    /// </summary>
    private static bool IsKnownSystemField(string name) => name switch
    {
        "createdon" or "modifiedon" or "createdby" or "modifiedby"
            or "ownerid" or "owningbusinessunit" or "owninguser" or "owningteam"
            or "statecode" or "statuscode" or "versionnumber" or "importsequencenumber"
            or "overriddencreatedon" or "timezoneruleversionnumber" or "utcconversiontimezonecode"
            => true,
        _ => false,
    };

    private static List<string> NormalizeFieldList(string fields) =>
        string.IsNullOrWhiteSpace(fields)
            ? []
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToList();

    private static string BuildFetchXml(
        string tableLogicalName,
        List<string> columns,
        string sortField,
        bool descending,
        string filterCondition)
    {
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<fetch version=\"1.0\" output-format=\"xml-platform\" mapping=\"logical\">");
        xml.AppendLine($"  <entity name=\"{tableLogicalName}\">");

        foreach (var col in columns)
        {
            xml.AppendLine($"    <attribute name=\"{col}\" />");
        }

        if (!string.IsNullOrWhiteSpace(sortField))
        {
            xml.AppendLine($"    <order attribute=\"{sortField}\" descending=\"{descending.ToString().ToLowerInvariant()}\" />");
        }

        if (!string.IsNullOrWhiteSpace(filterCondition))
        {
            xml.AppendLine("    <filter type=\"and\">");
            xml.AppendLine($"      {filterCondition.Trim()}");
            xml.AppendLine("    </filter>");
        }

        xml.AppendLine("  </entity>");
        xml.AppendLine("</fetch>");

        return xml.ToString();
    }

    private static string BuildLayoutXml(
        List<string> columns,
        string primaryIdAttribute,
        string primaryNameAttribute,
        IReadOnlyDictionary<string, string> columnTypes)
    {
        var xml = new System.Text.StringBuilder();
        // Per MS Customize Views doc:
        //   <grid jump="..."> must be the primary name attribute logical name
        //   <row id="...">    must be the primary id attribute logical name
        // Past failure: hardcoded jump="name" and id="<GUID>" → grid rejected
        // by Dataverse on custom tables.
        xml.AppendLine($"<grid name=\"resultset\" object=\"1\" jump=\"{primaryNameAttribute}\" select=\"1\" icon=\"1\" preview=\"1\">");
        xml.AppendLine($"  <row name=\"result\" id=\"{primaryIdAttribute}\">");

        // Widths are pixels in Dataverse layoutxml, NOT percentages. OOB views use:
        //   primary name = 300, lookup/owner = 150, datetime = 125, picklist/state/status = 100,
        //   memo = 300, money/numeric = 100, default = 150. Past failure: hardcoded
        //   100/count → "33" for every cell, columns visibly tiny in the maker portal.
        foreach (var col in columns)
        {
            columnTypes.TryGetValue(col, out var type);
            var width = WidthForColumn(col, type ?? string.Empty, primaryNameAttribute);
            xml.AppendLine($"    <cell name=\"{col}\" width=\"{width}\" />");
        }

        xml.AppendLine("  </row>");
        xml.AppendLine("</grid>");

        return xml.ToString();
    }

    private static int WidthForColumn(string column, string attributeType, string primaryNameAttribute)
    {
        if (string.Equals(column, primaryNameAttribute, StringComparison.Ordinal))
            return 300;

        // System fields where AttributeType isn't surfaced via GetTableDetails
        switch (column)
        {
            case "createdon":
            case "modifiedon":
            case "overriddencreatedon":
                return 125;
            case "createdby":
            case "modifiedby":
            case "ownerid":
            case "owningbusinessunit":
            case "owninguser":
            case "owningteam":
                return 150;
            case "statecode":
            case "statuscode":
                return 100;
        }

        return attributeType switch
        {
            "DateTime" => 125,
            "Lookup" or "Customer" or "Owner" => 150,
            "Picklist" or "MultiSelectPicklist" or "State" or "Status" or "Boolean" => 100,
            "Memo" => 300,
            "Money" or "Integer" or "Decimal" or "Double" or "BigInt" => 100,
            "String" => 200,
            _ => 150,
        };
    }
}
