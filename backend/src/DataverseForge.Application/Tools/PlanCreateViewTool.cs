using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateViewTool
{
    [Description("Generate a plan for creating a Dataverse system view. Returns the FetchXML, LayoutXML, "
        + "OData JSON payload, warnings, and risk level. Does NOT create the view — use execute_create_view for that.")]
    public Task<ViewCreationPlan> PlanCreateViewAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("The display name for the view (e.g., 'Active Work Orders')")] string viewName,
        [Description("Description of the view")] string description,
        [Description("Comma-separated list of field logical names to display as columns")] string columns,
        [Description("The field logical name to sort by (e.g., 'createdon')")] string sortField,
        [Description("Sort direction: 'ascending' or 'descending'")] string sortDirection,
        [Description("FetchXML filter condition (e.g., '<condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />'), empty for no filter")] string filterCondition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var columnList = string.IsNullOrWhiteSpace(columns)
            ? new List<string>()
            : columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        // Validate column count
        if (columnList.Count > 7)
            warnings.Add($"Views should have 5-7 columns max to avoid horizontal scroll. Current count: {columnList.Count}.");

        if (columnList.Count == 0)
            warnings.Add("View has no columns defined.");

        // Validate sort direction
        var descending = sortDirection.Equals("descending", StringComparison.OrdinalIgnoreCase);

        // Build FetchXML
        var fetchXml = BuildFetchXml(tableLogicalName, columnList, sortField, descending, filterCondition);

        // Build LayoutXML
        var layoutXml = BuildLayoutXml(columnList);

        // Build OData payload
        var payload = new Dictionary<string, object>
        {
            ["returnedtypecode"] = tableLogicalName,
            ["name"] = viewName,
            ["description"] = description,
            ["querytype"] = 0, // Public view
            ["fetchxml"] = fetchXml,
            ["layoutxml"] = layoutXml
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new ViewCreationPlan(
            TableLogicalName: tableLogicalName,
            ViewName: viewName,
            Description: description,
            FetchXml: fetchXml,
            LayoutXml: layoutXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "green"));
    }

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

    private static string BuildLayoutXml(List<string> columns)
    {
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<grid name=\"resultset\" object=\"1\" jump=\"name\" select=\"1\" icon=\"1\" preview=\"1\">");
        xml.AppendLine("  <row name=\"result\" id=\"{ID}\">");

        var width = columns.Count > 0 ? 100 / columns.Count : 100;
        foreach (var col in columns)
        {
            xml.AppendLine($"    <cell name=\"{col}\" width=\"{width}\" />");
        }

        xml.AppendLine("  </row>");
        xml.AppendLine("</grid>");

        return xml.ToString();
    }
}
