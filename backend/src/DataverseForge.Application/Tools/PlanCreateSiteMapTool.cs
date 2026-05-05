using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans creation of a Dataverse sitemap. Takes a structured spec
/// (areas[].groups[].subareas[]), validates it, builds the SiteMap XML, runs
/// the XML through the bundled SiteMap.xsd, and stores the OData payload
/// server-side under a planId.
///
/// Risk level: yellow — sitemaps are recoverable (you can patch them after
/// the fact). Goes orange when shipping with no sitemapnameunique because
/// duplicates rejected by Dataverse cost an approval cycle to fix.
/// </summary>
public class PlanCreateSiteMapTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    XsdValidator xsdValidator)
{
    [Description("Generate a plan for creating a Dataverse sitemap. Returns a planId, the XML preview, "
        + "warnings, and risk level (yellow). Pass the planId verbatim to execute_create_sitemap. "
        + "After execute, call attach_sitemap_to_app to attach it to a specific model-driven app. "
        + "areasJson is a JSON object: {areas:[{id,title?,icon?,showGroups,groups:[{id,title?,subareas:["
        + "{id,title?,entity?,url?,pageId?,type?,icon?,privileges:[{entity,privilege}]}]}]}]}. "
        + "Each subarea points to EXACTLY ONE of entity (table logical name), url (custom URL), "
        + "or pageId (custom page id). The planner runs SiteMap.xsd validation and rejects with "
        + "line/column errors if the XML is invalid.")]
    public async Task<SiteMapCreationPlan> PlanCreateSiteMapAsync(
        [Description("Display name (e.g., 'Demo Forge Sitemap'). Stored on the sitemap row's sitemapname column.")] string siteMapName,
        [Description("Unique name (must be globally unique within the environment, lowercase). e.g. 'eppc_DemoForgeSitemap'. Lowercased automatically.")] string siteMapNameUnique,
        [Description("Structured layout JSON — see tool description for the shape.")] string areasJson,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(siteMapName))
            return Reject(siteMapName, siteMapNameUnique, "siteMapName is required.");
        if (string.IsNullOrWhiteSpace(siteMapNameUnique))
            return Reject(siteMapName, siteMapNameUnique, "siteMapNameUnique is required (publisher-prefixed unique name, e.g. eppc_DemoSitemap).");

        var normalizedUnique = siteMapNameUnique.Trim();
        if (!string.Equals(normalizedUnique, normalizedUnique.ToLowerInvariant(), StringComparison.Ordinal))
        {
            warnings.Add($"siteMapNameUnique '{siteMapNameUnique}' contained uppercase characters — lowercased to '{normalizedUnique.ToLowerInvariant()}'.");
            normalizedUnique = normalizedUnique.ToLowerInvariant();
        }

        if (string.IsNullOrWhiteSpace(areasJson))
            return Reject(siteMapName, normalizedUnique, "areasJson is required (one Area minimum).");

        SiteMapLayoutSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<SiteMapLayoutSpec>(areasJson, _jsonOptions);
        }
        catch (JsonException jx)
        {
            return Reject(siteMapName, normalizedUnique,
                $"areasJson is not valid JSON: {jx.Message}. See the tool description for the expected shape.");
        }

        if (spec is null)
            return Reject(siteMapName, normalizedUnique, "areasJson parsed to null — supply at least one Area.");

        var shapeErrors = SiteMapXmlBuilder.Validate(spec);
        if (shapeErrors.Count > 0)
        {
            return Reject(siteMapName, normalizedUnique,
                "Spec invalid — " + string.Join(" | ", shapeErrors));
        }

        var siteMapXml = SiteMapXmlBuilder.Render(spec);

        var xsd = xsdValidator.Validate(siteMapXml, "SiteMap.xsd");
        if (!xsd.IsValid)
        {
            return Reject(siteMapName, normalizedUnique,
                "Generated SiteMap XML failed XSD validation: " + string.Join(" | ", xsd.Errors));
        }

        var payload = new Dictionary<string, object>
        {
            ["sitemapname"] = siteMapName,
            ["sitemapnameunique"] = normalizedUnique,
            ["sitemapxml"] = siteMapXml,
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"create_sitemap_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new SiteMapCreationPlan(
            PlanId: planId,
            SiteMapName: siteMapName,
            SiteMapNameUnique: normalizedUnique,
            SiteMapXml: siteMapXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow");
    }

    private static SiteMapCreationPlan Reject(string name, string nameUnique, string reason) =>
        new(
            PlanId: $"rejected_create_sitemap_{Guid.NewGuid():N}",
            SiteMapName: name ?? string.Empty,
            SiteMapNameUnique: nameUnique ?? string.Empty,
            SiteMapXml: string.Empty,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: "red");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
