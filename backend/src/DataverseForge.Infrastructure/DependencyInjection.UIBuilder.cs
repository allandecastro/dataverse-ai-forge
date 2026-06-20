using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Infrastructure.AzureAI;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.AgUi;
using DataverseForge.Infrastructure.Middleware;
using DataverseForge.Infrastructure.Settings;
using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure;

public static partial class DependencyInjection
{
    public static IServiceCollection AddUIBuilder(this IServiceCollection services)
    {
        services.AddScoped<UIBuilderAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var getFormsListTool = new GetFormsListTool(dataverseClient);
            var getViewsListTool = new GetViewsListTool(dataverseClient);
            var getFormDefinitionTool = new GetFormDefinitionTool(dataverseClient);
            var planCreateFormTool = new PlanCreateFormTool(dataverseClient, projectStateStore, sessionContext);
            var executeCreateFormTool = new ExecuteCreateFormTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateFormTool>>());
            var planUpdateFormTool = new PlanUpdateFormTool(dataverseClient, projectStateStore, sessionContext);
            var executeUpdateFormTool = new ExecuteUpdateFormTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateFormTool>>());
            var copyFormTool = new CopyFormTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<CopyFormTool>>());
            var planCreateViewTool = new PlanCreateViewTool(dataverseClient, projectStateStore, sessionContext);
            var executeCreateViewTool = new ExecuteCreateViewTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateViewTool>>());
            var planUpdateViewTool = new PlanUpdateViewTool(dataverseClient, projectStateStore, sessionContext);
            var executeUpdateViewTool = new ExecuteUpdateViewTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateViewTool>>());
            var deleteViewTool = new DeleteViewTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteViewTool>>());
            // Direct delete for forms (parity with views/charts). Approval is the agent's
            // responsibility — the tool itself doesn't gate.
            var deleteFormTool = new DeleteFormTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteFormTool>>());
            // Charts (savedqueryvisualization). Org-owned only; UIBuilder owns the
            // create/update/delete + Tester owns verify.
            var getChartsListTool = new GetChartsListTool(dataverseClient);
            var planCreateChartTool = new PlanCreateChartTool(dataverseClient, projectStateStore, sessionContext);
            var executeCreateChartTool = new ExecuteCreateChartTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateChartTool>>());
            var planUpdateChartTool = new PlanUpdateChartTool(dataverseClient, projectStateStore, sessionContext);
            var executeUpdateChartTool = new ExecuteUpdateChartTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateChartTool>>());
            var deleteChartTool = new DeleteChartTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteChartTool>>());
            // Dashboards (org-owned systemforms with type=0) — UIBuilder owns CRUD,
            // Tester owns verify. Same plan-by-reference + publish-after-write
            // shape as forms / charts.
            var getDashboardsListTool = new GetDashboardsListTool(dataverseClient);
            var getDashboardDefinitionTool = new GetDashboardDefinitionTool(dataverseClient);
            var planCreateDashboardTool = new PlanCreateDashboardTool(dataverseClient, projectStateStore, sessionContext);
            var executeCreateDashboardTool = new ExecuteCreateDashboardTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateDashboardTool>>());
            var planUpdateDashboardTool = new PlanUpdateDashboardTool(dataverseClient, projectStateStore, sessionContext);
            var executeUpdateDashboardTool = new ExecuteUpdateDashboardTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateDashboardTool>>());
            var deleteDashboardTool = new DeleteDashboardTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteDashboardTool>>());
            var requestApprovalTool = new RequestApprovalTool(projectStateStore, sessionContext);
            var discardPendingPlanTool = new DiscardPendingPlanTool(projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DiscardPendingPlanTool>>());
            var listPendingPlansTool = new ListPendingPlansTool(projectStateStore, sessionContext);
            // Query tools for UIBuilder — useful for previewing what an existing
            // view actually returns when planning a chart/dashboard cell that
            // references that view.
            var uibRunFetchXmlTool = new RunFetchXmlTool(dataverseClient);
            var uibRunSavedQueryTool = new RunSavedQueryTool(dataverseClient);
            // Model-driven app read tools — Phase 1 of MDA management. UIBuilder owns
            // the read surface even though writes aren't wired yet, so we can answer
            // "what's inside the Sales Hub app?" directly without bouncing through SA.
            var uibGetAppsListTool = new GetAppsListTool(dataverseClient);
            var uibGetAppDetailsTool = new GetAppDetailsTool(dataverseClient);
            var uibGetAppFullTool = new GetAppFullTool(dataverseClient);
            var uibGetSiteMapForAppTool = new GetSiteMapForAppTool(dataverseClient);
            var uibListAppRolesTool = new ListAppRolesTool(dataverseClient);
            // Phase 2 — App CRUD + Validate + Publish. UIBuilder owns the write surface.
            var planCreateAppTool = new PlanCreateAppTool(dataverseClient, projectStateStore, sessionContext);
            var executeCreateAppTool = new ExecuteCreateAppTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateAppTool>>());
            var planUpdateAppTool = new PlanUpdateAppTool(dataverseClient, projectStateStore, sessionContext);
            var executeUpdateAppTool = new ExecuteUpdateAppTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateAppTool>>());
            var deleteAppTool = new DeleteAppTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteAppTool>>());
            var validateAppTool = new ValidateAppTool(dataverseClient);
            var publishAppTool = new PublishAppTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<PublishAppTool>>());
            // Phase 4 — SiteMap CRUD + Attach. XsdValidator is a shared singleton.
            var xsdValidator = sp.GetRequiredService<XsdValidator>();
            var planCreateSiteMapTool = new PlanCreateSiteMapTool(projectStateStore, sessionContext, xsdValidator);
            var executeCreateSiteMapTool = new ExecuteCreateSiteMapTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateSiteMapTool>>());
            var planUpdateSiteMapTool = new PlanUpdateSiteMapTool(projectStateStore, sessionContext, xsdValidator);
            var executeUpdateSiteMapTool = new ExecuteUpdateSiteMapTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateSiteMapTool>>());
            var attachSiteMapToAppTool = new AttachSiteMapToAppTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<AttachSiteMapToAppTool>>());
            // Phase 3 — components add/remove + standalone read.
            var addAppComponentsTool = new AddAppComponentsTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<AddAppComponentsTool>>());
            var removeAppComponentsTool = new RemoveAppComponentsTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<RemoveAppComponentsTool>>());
            var getAppComponentsTool = new GetAppComponentsTool(dataverseClient);
            // Phase 5 — security roles. Reads are shared with SA; the assign/unassign
            // M:N writes are UIB-only. Direct (no plan/execute split) but auto-record
            // a planId in ExecutedPlans so verify_app_role_assignment_against_plan
            // has something to diff against.
            var uibGetSecurityRolesTool = new GetSecurityRolesTool(dataverseClient, sp.GetRequiredService<ILogger<GetSecurityRolesTool>>());
            var uibGetSecurityRoleDetailsTool = new GetSecurityRoleDetailsTool(dataverseClient);
            var assignAppRoleTool = new AssignAppRoleTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<AssignAppRoleTool>>());
            var unassignAppRoleTool = new UnassignAppRoleTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<UnassignAppRoleTool>>());
            // Phase 6 — one-shot scaffold/teardown orchestrators. Compose the
            // lower-level Phase 2/3/4/5 primitives so the agent can answer
            // "build me a working app for these tables" in a single call.
            var scaffoldAppTool = new ScaffoldAppTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ScaffoldAppTool>>());
            var teardownAppTool = new TeardownAppTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<TeardownAppTool>>());
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(getFormsListTool.GetFormsListAsync),
                AIFunctionFactory.Create(getViewsListTool.GetViewsListAsync),
                AIFunctionFactory.Create(uibRunFetchXmlTool.RunFetchXmlAsync),
                AIFunctionFactory.Create(uibRunSavedQueryTool.RunSavedQueryAsync),
                AIFunctionFactory.Create(getFormDefinitionTool.GetFormDefinitionAsync),
                AIFunctionFactory.Create(planCreateFormTool.PlanCreateFormAsync),
                AIFunctionFactory.Create(executeCreateFormTool.ExecuteCreateFormAsync),
                AIFunctionFactory.Create(planUpdateFormTool.PlanUpdateFormAsync),
                AIFunctionFactory.Create(executeUpdateFormTool.ExecuteUpdateFormAsync),
                AIFunctionFactory.Create(copyFormTool.CopyFormAsync),
                AIFunctionFactory.Create(planCreateViewTool.PlanCreateViewAsync),
                AIFunctionFactory.Create(executeCreateViewTool.ExecuteCreateViewAsync),
                AIFunctionFactory.Create(planUpdateViewTool.PlanUpdateViewAsync),
                AIFunctionFactory.Create(executeUpdateViewTool.ExecuteUpdateViewAsync),
                AIFunctionFactory.Create(deleteViewTool.DeleteViewAsync),
                AIFunctionFactory.Create(deleteFormTool.DeleteFormAsync),
                AIFunctionFactory.Create(getChartsListTool.GetChartsListAsync),
                AIFunctionFactory.Create(planCreateChartTool.PlanCreateChartAsync),
                AIFunctionFactory.Create(executeCreateChartTool.ExecuteCreateChartAsync),
                AIFunctionFactory.Create(planUpdateChartTool.PlanUpdateChartAsync),
                AIFunctionFactory.Create(executeUpdateChartTool.ExecuteUpdateChartAsync),
                AIFunctionFactory.Create(deleteChartTool.DeleteChartAsync),
                AIFunctionFactory.Create(getDashboardsListTool.GetDashboardsListAsync),
                AIFunctionFactory.Create(getDashboardDefinitionTool.GetDashboardDefinitionAsync),
                AIFunctionFactory.Create(planCreateDashboardTool.PlanCreateDashboardAsync),
                AIFunctionFactory.Create(executeCreateDashboardTool.ExecuteCreateDashboardAsync),
                AIFunctionFactory.Create(planUpdateDashboardTool.PlanUpdateDashboardAsync),
                AIFunctionFactory.Create(executeUpdateDashboardTool.ExecuteUpdateDashboardAsync),
                AIFunctionFactory.Create(deleteDashboardTool.DeleteDashboardAsync),
                AIFunctionFactory.Create(uibGetAppsListTool.GetAppsListAsync),
                AIFunctionFactory.Create(uibGetAppDetailsTool.GetAppDetailsAsync),
                AIFunctionFactory.Create(uibGetAppFullTool.GetAppFullAsync),
                AIFunctionFactory.Create(uibGetSiteMapForAppTool.GetSiteMapForAppAsync),
                AIFunctionFactory.Create(uibListAppRolesTool.ListAppRolesAsync),
                AIFunctionFactory.Create(planCreateAppTool.PlanCreateAppAsync),
                AIFunctionFactory.Create(executeCreateAppTool.ExecuteCreateAppAsync),
                AIFunctionFactory.Create(planUpdateAppTool.PlanUpdateAppAsync),
                AIFunctionFactory.Create(executeUpdateAppTool.ExecuteUpdateAppAsync),
                AIFunctionFactory.Create(deleteAppTool.DeleteAppAsync),
                AIFunctionFactory.Create(validateAppTool.ValidateAppAsync),
                AIFunctionFactory.Create(publishAppTool.PublishAppAsync),
                AIFunctionFactory.Create(planCreateSiteMapTool.PlanCreateSiteMapAsync),
                AIFunctionFactory.Create(executeCreateSiteMapTool.ExecuteCreateSiteMapAsync),
                AIFunctionFactory.Create(planUpdateSiteMapTool.PlanUpdateSiteMapAsync),
                AIFunctionFactory.Create(executeUpdateSiteMapTool.ExecuteUpdateSiteMapAsync),
                AIFunctionFactory.Create(attachSiteMapToAppTool.AttachSiteMapToAppAsync),
                AIFunctionFactory.Create(addAppComponentsTool.AddAppComponentsAsync),
                AIFunctionFactory.Create(removeAppComponentsTool.RemoveAppComponentsAsync),
                AIFunctionFactory.Create(getAppComponentsTool.GetAppComponentsAsync),
                AIFunctionFactory.Create(uibGetSecurityRolesTool.GetSecurityRolesAsync),
                AIFunctionFactory.Create(uibGetSecurityRoleDetailsTool.GetSecurityRoleDetailsAsync),
                AIFunctionFactory.Create(assignAppRoleTool.AssignAppRoleAsync),
                AIFunctionFactory.Create(unassignAppRoleTool.UnassignAppRoleAsync),
                AIFunctionFactory.Create(scaffoldAppTool.ScaffoldAppAsync),
                AIFunctionFactory.Create(teardownAppTool.TeardownAppAsync),
                AIFunctionFactory.Create(requestApprovalTool.RequestApprovalAsync),
                AIFunctionFactory.Create(discardPendingPlanTool.DiscardPendingPlanAsync),
                AIFunctionFactory.Create(listPendingPlansTool.ListPendingPlansAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Tester }.AsReadOnly();

            return new UIBuilderAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.UIBuilder),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<UIBuilderAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<UIBuilderAgent>());

        return services;
    }
}
