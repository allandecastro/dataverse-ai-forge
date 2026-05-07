using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Tester verifier for dashboards. Same shape as VerifyChartAgainstPlanTool —
/// loads the planned payload from state.ExecutedPlans (under the
/// create_dashboard_* / update_dashboard_* planId) and diffs against the live
/// dashboard's name / description / FormXML.
/// </summary>
public class VerifyDashboardAgainstPlanTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Verify a Dataverse dashboard against the planned spec. Pass the planId from "
        + "plan_create_dashboard / plan_update_dashboard and the dashboardId. Returns PASS / FAIL / "
        + "PARTIAL with discrepancies (name/description/FormXML compared whitespace-normalized).")]
    public async Task<DashboardVerificationResult> VerifyDashboardAgainstPlanAsync(
        [Description("planId from plan_create_dashboard or plan_update_dashboard")] string planId,
        [Description("The dashboard's GUID (formid)")] string dashboardId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(dashboardId, out var parsedId) || parsedId == Guid.Empty)
        {
            return new DashboardVerificationResult(
                Guid.Empty, planId, "FAIL",
                new List<string> { $"dashboardId '{dashboardId}' is not a valid GUID." }.AsReadOnly(),
                null, null);
        }

        // F-35: rejected_* planIds get a clear "never executed" message.
        if (planId.StartsWith("rejected_dashboard_invalid_", StringComparison.Ordinal))
        {
            return new DashboardVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"REJECTED PLAN: '{planId}' was rejected at planning time and never reached Dataverse — re-plan with valid inputs." }.AsReadOnly(),
                null, null);
        }

        if (!(planId.StartsWith("create_dashboard_", StringComparison.Ordinal)
              || planId.StartsWith("update_dashboard_", StringComparison.Ordinal)))
        {
            return new DashboardVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"WRONG PLAN TYPE: '{planId}' is not a dashboard plan. Use the matching verify_*_against_plan instead." }.AsReadOnly(),
                null, null);
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!state.ExecutedPlans.TryGetValue(planId, out var plannedPayload))
        {
            var pendingHint = state.PendingPlans.ContainsKey(planId)
                ? " The plan is still pending — execute_*_dashboard hasn't been called yet."
                : "";
            return new DashboardVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"Plan '{planId}' not found in ExecutedPlans.{pendingHint}" }.AsReadOnly(),
                null, null);
        }

        DashboardDefinition live;
        try
        {
            live = await dataverseClient.GetDashboardDefinitionAsync(parsedId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DashboardVerificationResult(
                parsedId, planId, "FAIL",
                new List<string> { $"Could not fetch dashboard {parsedId}: {ex.Message}" }.AsReadOnly(),
                null, null);
        }

        var planned = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(plannedPayload)
            ?? new Dictionary<string, JsonElement>();

        var discrepancies = new List<string>();

        var plannedName = TryGetString(planned, "name");
        if (plannedName is not null && !string.Equals(plannedName, live.Name, StringComparison.Ordinal))
            discrepancies.Add($"name: expected '{plannedName}', got '{live.Name}'");

        var plannedDescription = TryGetString(planned, "description");
        if (plannedDescription is not null && !string.Equals(plannedDescription, live.Description, StringComparison.Ordinal))
            discrepancies.Add($"description: expected '{plannedDescription}', got '{live.Description}'");

        var plannedFormXml = TryGetString(planned, "formxml") ?? string.Empty;
        if (!XmlEquivalent(plannedFormXml, live.FormXml))
            discrepancies.Add("formxml differs from plan (whitespace-normalized).");

        var verdict = discrepancies.Count == 0 ? "PASS"
            : discrepancies.Any(d => d.StartsWith("name:")) ? "FAIL"
            : "PARTIAL";

        return new DashboardVerificationResult(
            DashboardId: parsedId,
            PlanId: planId,
            Verdict: verdict,
            Discrepancies: discrepancies.AsReadOnly(),
            PlanFormXml: plannedFormXml,
            ActualFormXml: live.FormXml);
    }

    private static string? TryGetString(IReadOnlyDictionary<string, JsonElement> dict, string key)
    {
        if (!dict.TryGetValue(key, out var v)) return null;
        return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    private static bool XmlEquivalent(string a, string b)
    {
        return Normalize(a) == Normalize(b);
        static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}
