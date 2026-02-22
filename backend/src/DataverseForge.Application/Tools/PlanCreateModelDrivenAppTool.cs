using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateModelDrivenAppTool
{
    [Description("Generate a plan for creating a Dataverse model-driven app. Returns the SiteMap XML, "
        + "OData JSON payload, warnings, and risk level. Does NOT create the app — use execute_create_model_driven_app for that.")]
    public Task<ModelDrivenAppCreationPlan> PlanCreateModelDrivenAppAsync(
        [Description("The display name for the app (e.g., 'Field Service Hub')")] string appName,
        [Description("The unique name for the app (no spaces, e.g., 'cr123_fieldservicehub')")] string appUniqueName,
        [Description("Description of the app")] string description,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        [Description("Comma-separated list of area definitions (format: 'areaId:areaTitle:groupId:groupTitle:tableLogicalName1;tableLogicalName2')")] string siteMapDefinition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (appUniqueName.Contains(' '))
            warnings.Add("App unique name should not contain spaces.");

        if (!appUniqueName.StartsWith(publisherPrefix + "_", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"App unique name '{appUniqueName}' does not start with publisher prefix '{publisherPrefix}_'.");

        var areas = ParseSiteMapDefinition(siteMapDefinition);

        if (areas.Count == 0)
            warnings.Add("No sitemap areas defined. The app will have an empty navigation.");

        // Build SiteMap XML
        var siteMapXml = BuildSiteMapXml(areas);

        // Build OData payload
        var payload = new Dictionary<string, object>
        {
            ["name"] = appName,
            ["uniquename"] = appUniqueName,
            ["description"] = description,
            ["clienttype"] = 4, // Model-driven app
            ["sitemapxml"] = siteMapXml
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new ModelDrivenAppCreationPlan(
            AppName: appName,
            AppUniqueName: appUniqueName,
            Description: description,
            SiteMapXml: siteMapXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
    }

    private static List<SiteMapArea> ParseSiteMapDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return [];

        var areas = new List<SiteMapArea>();
        foreach (var areaDef in definition.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = areaDef.Split(':');
            if (parts.Length >= 5)
            {
                var tables = parts[4].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                areas.Add(new SiteMapArea(parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim(), tables));
            }
        }
        return areas;
    }

    private static string BuildSiteMapXml(List<SiteMapArea> areas)
    {
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<SiteMap>");

        foreach (var area in areas)
        {
            xml.AppendLine($"  <Area Id=\"{area.AreaId}\" Title=\"{area.AreaTitle}\">");
            xml.AppendLine($"    <Group Id=\"{area.GroupId}\" Title=\"{area.GroupTitle}\">");

            foreach (var table in area.Tables)
            {
                xml.AppendLine($"      <SubArea Id=\"nav_{table}\" Entity=\"{table}\" />");
            }

            xml.AppendLine("    </Group>");
            xml.AppendLine("  </Area>");
        }

        xml.AppendLine("</SiteMap>");

        return xml.ToString();
    }

    private record SiteMapArea(string AreaId, string AreaTitle, string GroupId, string GroupTitle, List<string> Tables);
}
