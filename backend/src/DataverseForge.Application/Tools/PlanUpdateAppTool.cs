using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans an update to an existing model-driven app (rename / re-describe /
/// flip clientType / change navigationType). uniquename is intentionally NOT
/// updatable here — Dataverse rejects PATCH on the unique-name column for
/// existing rows; renaming-by-uniquename is a delete + recreate flow.
/// </summary>
public class PlanUpdateAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for updating an existing model-driven app — display name, "
        + "description, clientType, navigationType. uniqueName cannot be changed (delete + "
        + "recreate if you need that). Returns a planId; pass it to execute_update_app after "
        + "approval. Risk level: yellow (recoverable; the app stays in place).")]
    public async Task<AppUpdatePlan> PlanUpdateAppAsync(
        [Description("appmoduleid (GUID) of the app to update — get from get_apps_list.")] string appId,
        [Description("New display name (or pass the existing name to leave it unchanged).")] string name,
        [Description("New description (free text).")] string description,
        CancellationToken cancellationToken,
        [Description("Client type. 4 = Unified Interface, 8 = Outlook.")] int clientType = 4,
        [Description("Navigation type. 0 = Single session, 1 = Multi session.")] int navigationType = 0)
    {
        var warnings = new List<string>();

        if (!Guid.TryParse(appId, out var parsedId) || parsedId == Guid.Empty)
            return Reject(Guid.Empty, name, description, "appId must be a valid GUID — call get_apps_list to retrieve it.");

        if (string.IsNullOrWhiteSpace(name))
            return Reject(parsedId, name, description, "name is required.");

        if (clientType is not (4 or 8))
            return Reject(parsedId, name, description,
                $"clientType {clientType} is not supported. Use 4 (Unified Interface) or 8 (Outlook).");
        if (navigationType is not (0 or 1))
            return Reject(parsedId, name, description,
                $"navigationType {navigationType} is not supported. Use 0 (Single session) or 1 (Multi session).");

        // Pre-flight: best-effort existence check. We DON'T hard-reject on a
        // null read here — real session 2026-05-05 had a freshly-created
        // appmodule (publish failed → app exists in maker portal but
        // GetAppAsync returns null and GetAppsList omits it for several
        // minutes). Hard-rejecting trapped the user in a recreate loop.
        // Downstream UpdateAppAsync uses `If-Match: *` which still rejects
        // a truly-missing id, so we don't lose safety.
        try
        {
            var existing = await dataverseClient.GetAppAsync(parsedId, cancellationToken);
            if (existing is null)
            {
                warnings.Add($"Could not verify app {parsedId} via GetAppAsync — Dataverse occasionally hides freshly-created apps from direct reads for several minutes. Proceeding with the plan; the PATCH will fail cleanly if the id is truly invalid.");
            }
            else if (existing.IsManaged)
            {
                warnings.Add($"App '{existing.Name}' is MANAGED — Dataverse will reject a PATCH on managed apps. Update the source environment, not this one.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Could not verify app existence ({ex.Message}) — proceeding with the plan anyway.");
        }

        var payload = new Dictionary<string, object>
        {
            ["name"] = name,
            ["description"] = description ?? string.Empty,
            ["clienttype"] = clientType,
            ["navigationtype"] = navigationType,
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"update_app_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new AppUpdatePlan(
            PlanId: planId,
            AppId: parsedId,
            Name: name,
            Description: description ?? string.Empty,
            ClientType: clientType,
            NavigationType: navigationType,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Yellow);
    }

    private static AppUpdatePlan Reject(Guid appId, string name, string description, string reason) =>
        new(
            PlanId: $"rejected_update_app_{Guid.NewGuid():N}",
            AppId: appId,
            Name: name ?? string.Empty,
            Description: description ?? string.Empty,
            ClientType: 0,
            NavigationType: 0,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: PlanRisk.Red);
}
