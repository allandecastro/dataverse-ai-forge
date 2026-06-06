using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans creation of a new model-driven app (appmodule). Validates the
/// uniqueName / clientType / navigationType inputs, lowercases the unique name
/// (Dataverse logical-name rule), and stores the OData JSON payload server-side
/// under a planId so the agent passes only the id at execute time.
///
/// Risk level: orange — creating an app is a high-impact, hard-to-reverse
/// change (new top-level navigation surface for end-users).
/// </summary>
public class PlanCreateAppTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    /// <summary>
    /// OOB icon web resource that ships with every Dataverse environment. Used
    /// as the default value for <c>webresourceid</c> on appmodule create —
    /// Dataverse rejects the create with "Attribute 'webresourceid' cannot be
    /// NULL" if we don't supply one. The GUID varies per environment, so we
    /// resolve it by name at plan time.
    /// </summary>
    private const string DefaultAppIconWebResourceName = "msdyn_/Images/AppModule_Default_Icon.png";

    [Description("Generate a plan for creating a model-driven app (appmodule). "
        + "Returns a planId, the resolved unique name, and risk level (orange — creating an app "
        + "is high-impact). Pass the planId verbatim to execute_create_app after approval. "
        + "uniqueName must start with the active solution's publisher prefix (e.g. eppc_SalesHub). "
        + "clientType: 4 = Unified Interface (default), 8 = Outlook. "
        + "navigationType: 0 = Single session (default), 1 = Multi session (workspace-style with tabs). "
        + "The plan auto-resolves the OOB default icon web resource (Dataverse rejects the create "
        + "without one). After execute, attach a sitemap (plan_create_sitemap + "
        + "attach_sitemap_to_app) before the app can be opened by users — ValidateApp will "
        + "report 'no sitemap' on a freshly-created app, which is expected.")]
    public async Task<AppCreationPlan> PlanCreateAppAsync(
        [Description("Display name shown in the app picker (e.g., 'Sales Hub')")] string name,
        [Description("Logical / unique name. Must include publisher prefix — e.g. eppc_SalesHub. "
            + "Lowercased automatically; Dataverse rejects mixed-case logical names.")] string uniqueName,
        [Description("Description of the app's purpose (free text).")] string description,
        CancellationToken cancellationToken,
        [Description("Client type. 4 = Unified Interface (default), 8 = Outlook.")] int clientType = 4,
        [Description("Navigation type. 0 = Single session (default), 1 = Multi session (workspace-style, e.g. Copilot Service workspace).")] int navigationType = 0,
        [Description("Optional GUID of an image web resource to use as the app icon. Leave empty to "
            + "use the OOB default (msdyn_/Images/AppModule_Default_Icon.png) resolved at plan time.")]
        string webResourceId = "")
    {
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
            return Reject(name, uniqueName, description, "name is required.");
        if (string.IsNullOrWhiteSpace(uniqueName))
            return Reject(name, uniqueName, description, "uniqueName is required (publisher-prefixed, e.g. eppc_SalesHub).");

        // Dataverse rejects mixed-case logical / unique names. Lowercase
        // automatically and warn if we changed it (the agent should learn).
        var normalizedUniqueName = uniqueName.Trim();
        if (!string.Equals(normalizedUniqueName, normalizedUniqueName.ToLowerInvariant(), StringComparison.Ordinal))
        {
            warnings.Add($"uniqueName '{uniqueName}' contained uppercase characters — lowercased to '{normalizedUniqueName.ToLowerInvariant()}'. Dataverse logical names are lowercase.");
            normalizedUniqueName = normalizedUniqueName.ToLowerInvariant();
        }

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        // Prefix check — strongly recommended, not enforced (some users want to
        // create an app under a different publisher). Surface as a warning.
        if (!string.IsNullOrWhiteSpace(state.PublisherPrefix))
        {
            var expectedPrefix = state.FullPrefix; // already includes trailing "_"
            if (!normalizedUniqueName.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                warnings.Add($"uniqueName '{normalizedUniqueName}' does not start with the active publisher prefix '{expectedPrefix}'. Apps without a prefix end up in the default solution.");
            }
        }

        if (clientType is not (4 or 8))
            return Reject(name, normalizedUniqueName, description,
                $"clientType {clientType} is not supported. Use 4 (Unified Interface) or 8 (Outlook).");
        if (navigationType is not (0 or 1))
            return Reject(name, normalizedUniqueName, description,
                $"navigationType {navigationType} is not supported. Use 0 (Areas) or 1 (Workspaces).");

        // Resolve the icon web resource. webresourceid is REQUIRED on create —
        // Dataverse rejects with "Attribute 'webresourceid' cannot be NULL"
        // otherwise. Caller can supply their own; default to the OOB icon.
        Guid resolvedWebResourceId;
        if (!string.IsNullOrWhiteSpace(webResourceId))
        {
            if (!Guid.TryParse(webResourceId, out resolvedWebResourceId) || resolvedWebResourceId == Guid.Empty)
                return Reject(name, normalizedUniqueName, description,
                    $"webResourceId '{webResourceId}' is not a valid GUID. Pass a webresourceid GUID or leave empty to use the OOB default.");
        }
        else
        {
            try
            {
                var resolved = await dataverseClient.ResolveWebResourceIdByNameAsync(DefaultAppIconWebResourceName, cancellationToken);
                if (resolved is null || resolved.Value == Guid.Empty)
                {
                    return Reject(name, normalizedUniqueName, description,
                        $"Could not find the OOB default icon web resource '{DefaultAppIconWebResourceName}' in this environment. "
                        + "Pass a custom webResourceId (any image web resource) or provision the OOB icon first.");
                }
                resolvedWebResourceId = resolved.Value;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Reject(name, normalizedUniqueName, description,
                    $"Failed to resolve the OOB default icon web resource: {ex.Message}. Pass a custom webResourceId or retry.");
            }
        }

        var payload = new Dictionary<string, object>
        {
            ["name"] = name,
            ["uniquename"] = normalizedUniqueName,
            ["description"] = description ?? string.Empty,
            ["clienttype"] = clientType,
            ["navigationtype"] = navigationType,
            // Required on create. On appmodule, `webresourceid` is a
            // UniqueIdentifierAttribute (plain Edm.Guid), NOT a LookupAttribute —
            // Dataverse rejects `@odata.bind` form here with "ODataPrimitiveValue
            // was instantiated with a value of type Microsoft.OData.ODataEntityReferenceLink".
            // Write the raw GUID string directly.
            ["webresourceid"] = resolvedWebResourceId.ToString(),
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"create_app_{Guid.NewGuid():N}";
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new AppCreationPlan(
            PlanId: planId,
            Name: name,
            UniqueName: normalizedUniqueName,
            Description: description ?? string.Empty,
            ClientType: clientType,
            NavigationType: navigationType,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Orange);
    }

    private static AppCreationPlan Reject(string name, string uniqueName, string description, string reason) =>
        new(
            PlanId: $"rejected_create_app_{Guid.NewGuid():N}",
            Name: name ?? string.Empty,
            UniqueName: uniqueName ?? string.Empty,
            Description: description ?? string.Empty,
            ClientType: 0,
            NavigationType: 0,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { "REJECTED: " + reason }.AsReadOnly(),
            RiskLevel: PlanRisk.Red);
}
