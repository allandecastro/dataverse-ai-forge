using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans an update to an existing org-owned dashboard. Same XML builder as
/// create — only the HTTP verb differs (PATCH systemforms({id})). Caller
/// passes the existing dashboardId from get_dashboards_list.
/// </summary>
public class PlanUpdateDashboardTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for UPDATING an existing org-owned Dataverse dashboard. "
        + "Returns a planId; pass it verbatim to execute_update_dashboard. "
        + "Use get_dashboards_list to find the dashboardId. tabsJson syntax matches plan_create_dashboard.")]
    public async Task<DashboardUpdatePlan> PlanUpdateDashboardAsync(
        [Description("The GUID of the existing dashboard to update (from get_dashboards_list)")] string dashboardId,
        [Description("New display name (pass the existing name if not renaming)")] string dashboardName,
        [Description("Description for the dashboard")] string description,
        [Description("Structured layout JSON — same shape as plan_create_dashboard")] string tabsJson,
        CancellationToken cancellationToken,
        [Description("Maximum total cells. Default 6.")] int maxControls = 6)
    {
        if (!Guid.TryParse(dashboardId, out var parsedId) || parsedId == Guid.Empty)
        {
            return Reject(Guid.Empty, dashboardName, description, "dashboardId must be a valid GUID from get_dashboards_list.");
        }

        if (string.IsNullOrWhiteSpace(tabsJson))
            return Reject(parsedId, dashboardName, description, "tabsJson is required.");

        DashboardLayoutSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<DashboardLayoutSpec>(tabsJson, _jsonOptions);
        }
        catch (JsonException jx)
        {
            return Reject(parsedId, dashboardName, description, $"tabsJson is not valid JSON: {jx.Message}");
        }

        if (spec is null || spec.Tabs is null || spec.Tabs.Count == 0)
            return Reject(parsedId, dashboardName, description, "tabsJson parsed but contained no tabs.");

        var validationErrors = DashboardXmlBuilder.Validate(spec, Math.Clamp(maxControls, 1, 20));
        if (validationErrors.Count > 0)
        {
            return Reject(parsedId, dashboardName, description, "Layout invalid — " + string.Join(" ", validationErrors));
        }

        var formXml = DashboardXmlBuilder.Render(spec);

        // PATCH payload — drop type and isdefault on update so we don't risk
        // accidentally re-typing an existing dashboard.
        var payload = new Dictionary<string, object>
        {
            ["name"] = dashboardName,
            ["description"] = description ?? string.Empty,
            ["formxml"] = formXml
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"update_dashboard_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new DashboardUpdatePlan(
            PlanId: planId,
            DashboardId: parsedId,
            DashboardName: dashboardName,
            Description: description ?? string.Empty,
            FormXml: formXml,
            WebApiPayloadJson: payloadJson,
            Warnings: new List<string>().AsReadOnly(),
            RiskLevel: "orange");
    }

    private static DashboardUpdatePlan Reject(Guid id, string name, string description, string reason)
        => new(
            PlanId: $"rejected_dashboard_invalid_{Guid.NewGuid():N}",
            DashboardId: id,
            DashboardName: name ?? string.Empty,
            Description: description ?? string.Empty,
            FormXml: string.Empty,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: "red");

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
