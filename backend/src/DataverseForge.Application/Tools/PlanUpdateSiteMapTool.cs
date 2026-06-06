using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans an update to an existing Dataverse sitemap. Replaces the
/// <c>sitemapxml</c> blob — sitemapname / sitemapnameunique are intentionally
/// not editable here (rename = delete + recreate).
/// </summary>
public class PlanUpdateSiteMapTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    XsdValidator xsdValidator)
{
    [Description("Generate a plan for updating an existing Dataverse sitemap. Returns a planId. "
        + "Pass it to execute_update_sitemap. The plan re-renders + XSD-validates the XML before "
        + "storing the payload. siteMapId comes from get_sitemap_for_app or get_apps_list. "
        + "Risk: yellow (recoverable; the sitemap stays in place if you patch back to the old XML).")]
    public async Task<SiteMapUpdatePlan> PlanUpdateSiteMapAsync(
        [Description("sitemapid (GUID) of the sitemap to update.")] string siteMapId,
        [Description("Display name for audit (must match the existing sitemap; not updatable).")] string siteMapName,
        [Description("Structured layout JSON — see plan_create_sitemap for the shape.")] string areasJson,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        if (!Guid.TryParse(siteMapId, out var parsedId) || parsedId == Guid.Empty)
            return Reject(Guid.Empty, siteMapName, "siteMapId must be a valid GUID.");

        if (string.IsNullOrWhiteSpace(areasJson))
            return Reject(parsedId, siteMapName, "areasJson is required.");

        SiteMapLayoutSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<SiteMapLayoutSpec>(areasJson, _jsonOptions);
        }
        catch (JsonException jx)
        {
            return Reject(parsedId, siteMapName, $"areasJson is not valid JSON: {jx.Message}.");
        }

        if (spec is null)
            return Reject(parsedId, siteMapName, "areasJson parsed to null — supply at least one Area.");

        var shapeErrors = SiteMapXmlBuilder.Validate(spec);
        if (shapeErrors.Count > 0)
        {
            return Reject(parsedId, siteMapName, "Spec invalid — " + string.Join(" | ", shapeErrors));
        }

        var siteMapXml = SiteMapXmlBuilder.Render(spec);

        var xsd = xsdValidator.Validate(siteMapXml, "SiteMap.xsd");
        if (!xsd.IsValid)
        {
            return Reject(parsedId, siteMapName,
                "Generated SiteMap XML failed XSD validation: " + string.Join(" | ", xsd.Errors));
        }

        var payload = new Dictionary<string, object>
        {
            ["sitemapxml"] = siteMapXml,
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"update_sitemap_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new SiteMapUpdatePlan(
            PlanId: planId,
            SiteMapId: parsedId,
            SiteMapName: siteMapName,
            SiteMapXml: siteMapXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Yellow);
    }

    private static SiteMapUpdatePlan Reject(Guid id, string name, string reason) =>
        new(
            PlanId: $"rejected_update_sitemap_{Guid.NewGuid():N}",
            SiteMapId: id,
            SiteMapName: name ?? string.Empty,
            SiteMapXml: string.Empty,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: PlanRisk.Red);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
