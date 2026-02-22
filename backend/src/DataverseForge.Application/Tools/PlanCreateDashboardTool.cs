using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateDashboardTool
{
    [Description("Generate a plan for creating a Dataverse dashboard. Returns the dashboard FormXML, "
        + "OData JSON payload, warnings, and risk level. Does NOT create the dashboard — use execute_create_dashboard for that.")]
    public Task<DashboardCreationPlan> PlanCreateDashboardAsync(
        [Description("The display name for the dashboard (e.g., 'Work Order Overview')")] string dashboardName,
        [Description("Description of the dashboard")] string description,
        [Description("Number of columns in the dashboard layout (1, 2, or 3)")] int columnCount,
        [Description("Comma-separated list of view names to include as components (format: 'tableName:viewName')")] string viewComponents,
        [Description("Comma-separated list of chart names to include (format: 'tableName:chartName'), empty if none")] string chartComponents,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (columnCount < 1 || columnCount > 3)
        {
            warnings.Add($"Column count {columnCount} is out of range (1-3). Defaulting to 2.");
            columnCount = 2;
        }

        var viewList = ParseComponentList(viewComponents);
        var chartList = ParseComponentList(chartComponents);

        var totalComponents = viewList.Count + chartList.Count;
        if (totalComponents > 6)
            warnings.Add($"Dashboard has {totalComponents} components. Consider limiting to 6 for readability.");

        if (viewList.Count == 0 && chartList.Count == 0)
            warnings.Add("Dashboard has no components defined.");

        // Build dashboard FormXML
        var formXml = BuildDashboardFormXml(dashboardName, columnCount, viewList, chartList);

        // Build OData payload — dashboards are stored as systemforms with type=0
        var payload = new Dictionary<string, object>
        {
            ["name"] = dashboardName,
            ["description"] = description,
            ["type"] = 0, // Dashboard type
            ["formxml"] = formXml
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new DashboardCreationPlan(
            DashboardName: dashboardName,
            Description: description,
            FormXml: formXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "green"));
    }

    private static List<(string Table, string Name)> ParseComponentList(string components)
    {
        if (string.IsNullOrWhiteSpace(components))
            return [];

        var result = new List<(string Table, string Name)>();
        foreach (var item in components.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split(':', 2);
            if (parts.Length == 2)
                result.Add((parts[0].Trim(), parts[1].Trim()));
        }
        return result;
    }

    private static string BuildDashboardFormXml(
        string dashboardName,
        int columnCount,
        List<(string Table, string Name)> views,
        List<(string Table, string Name)> charts)
    {
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<form>");
        xml.AppendLine("  <tabs>");
        xml.AppendLine("    <tab name=\"tab_dashboard\" id=\"{TAB_DASHBOARD}\">");
        xml.AppendLine("      <labels><label description=\"Dashboard\" languagecode=\"1033\" /></labels>");
        xml.AppendLine("      <columns>");

        var widthPercent = 100 / columnCount;
        var allComponents = new List<(string Table, string Name, string Type)>();
        foreach (var v in views)
            allComponents.Add((v.Table, v.Name, "view"));
        foreach (var c in charts)
            allComponents.Add((c.Table, c.Name, "chart"));

        for (var col = 0; col < columnCount; col++)
        {
            xml.AppendLine($"        <column width=\"{widthPercent}%\">");
            xml.AppendLine("          <sections>");

            // Distribute components across columns
            for (var i = col; i < allComponents.Count; i += columnCount)
            {
                var comp = allComponents[i];
                var sectionId = $"sec_{comp.Type}_{i}";
                xml.AppendLine($"            <section name=\"{sectionId}\" id=\"{{{sectionId.ToUpperInvariant()}}}\">");
                xml.AppendLine($"              <labels><label description=\"{comp.Name}\" languagecode=\"1033\" /></labels>");
                xml.AppendLine("              <rows><row><cell>");

                if (comp.Type == "view")
                {
                    xml.AppendLine($"                <control id=\"{sectionId}_control\" classid=\"{{E7A81278-8635-4D9E-8D4D-59480B391C5B}}\">");
                    xml.AppendLine("                  <parameters>");
                    xml.AppendLine($"                    <TargetEntityType>{comp.Table}</TargetEntityType>");
                    xml.AppendLine("                  </parameters>");
                    xml.AppendLine("                </control>");
                }
                else
                {
                    xml.AppendLine($"                <control id=\"{sectionId}_control\" classid=\"{{E7A81278-8635-4D9E-8D4D-59480B391C5B}}\">");
                    xml.AppendLine("                  <parameters>");
                    xml.AppendLine($"                    <TargetEntityType>{comp.Table}</TargetEntityType>");
                    xml.AppendLine("                    <ChartGridMode>Chart</ChartGridMode>");
                    xml.AppendLine("                  </parameters>");
                    xml.AppendLine("                </control>");
                }

                xml.AppendLine("              </cell></row></rows>");
                xml.AppendLine("            </section>");
            }

            xml.AppendLine("          </sections>");
            xml.AppendLine("        </column>");
        }

        xml.AppendLine("      </columns>");
        xml.AppendLine("    </tab>");
        xml.AppendLine("  </tabs>");
        xml.AppendLine("</form>");

        return xml.ToString();
    }
}
