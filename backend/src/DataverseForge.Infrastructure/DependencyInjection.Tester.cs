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
    public static IServiceCollection AddTester(this IServiceCollection services)
    {
        services.AddScoped<TesterAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var verifyTableTool = new VerifyTableCreationTool(dataverseClient);
            var verifyTableAgainstPlanTool = new VerifyTableAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var verifyFieldTool = new VerifyFieldCreationTool(dataverseClient);
            var verifyFieldAgainstPlanTool = new VerifyFieldAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var verifyOptionSetAgainstPlanTool = new VerifyGlobalOptionSetAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            // Chart verifier — same shape as the table/field/optionset verifiers,
            // compares the planned datadescription/presentationdescription XML
            // against the live chart record.
            var verifyChartAgainstPlanTool = new VerifyChartAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var getChartsListToolForTester = new GetChartsListTool(dataverseClient);
            // Dashboard verifier + read tools so Tester can spot-check what
            // actually shipped after execute_create_dashboard / execute_update_dashboard.
            var verifyDashboardAgainstPlanTool = new VerifyDashboardAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var getDashboardsListToolForTester = new GetDashboardsListTool(dataverseClient);
            var getDashboardDefinitionToolForTester = new GetDashboardDefinitionTool(dataverseClient);
            // App verifier + read tools so Tester can spot-check what
            // actually shipped after execute_create_app / execute_update_app.
            var verifyAppAgainstPlanTool = new VerifyAppAgainstPlanTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<VerifyAppAgainstPlanTool>>());
            var getAppsListToolForTester = new GetAppsListTool(dataverseClient);
            var getAppDetailsToolForTester = new GetAppDetailsTool(dataverseClient);
            var validateAppToolForTester = new ValidateAppTool(dataverseClient);
            // Phase 4 — sitemap verifier + read tool for Tester.
            var verifySiteMapAgainstPlanTool = new VerifySiteMapAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var getSiteMapForAppToolForTester = new GetSiteMapForAppTool(dataverseClient);
            // Phase 3 — components read for Tester (verify what was attached).
            var getAppComponentsToolForTester = new GetAppComponentsTool(dataverseClient);
            // Phase 5 — app-role-assignment verifier. Reads the planId stored
            // by assign_app_role / unassign_app_role and confirms the live M:N
            // membership matches the recorded action.
            var verifyAppRoleAssignmentTool = new VerifyAppRoleAssignmentAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var listAppRolesToolForTester = new ListAppRolesTool(dataverseClient);
            // Phase 6 — Security Builder verifiers.
            // - VerifySecurityRoleAgainstPlanTool: diffs the live role's name +
            //   privilege matrix against create / update plans
            //   (create_security_role_* / update_security_role_*).
            // - VerifyBulkPrivilegeGrantAgainstPlanTool: walks every role in a
            //   bulk grant plan and rolls per-role verdicts into a composite.
            //   Routes on grant_privilege_to_roles_* planIds.
            var verifySecurityRoleAgainstPlanTool = new VerifySecurityRoleAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var verifyBulkPrivilegeGrantAgainstPlanTool = new VerifyBulkPrivilegeGrantAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            // Phase 7 — AutomationBuilder verifier. Routes by planId prefix
            // (create_bpf_* / update_bpf_* / create_business_rule_* /
            // update_business_rule_*). v1 diffs metadata only (name, primary
            // entity, category); full XAML diffing deferred because the XAML
            // schema is fragile and Dataverse normalizes what it echoes back.
            var verifyWorkflowAgainstPlanTool = new VerifyWorkflowAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var verifyRelTool = new VerifyRelationshipCreationTool(dataverseClient);
            var verifyFormTool = new VerifyFormCreationTool(dataverseClient);
            var verifyViewTool = new VerifyViewCreationTool(dataverseClient);
            // Plan-keyed form + view verifiers. Mirror the table / field /
            // chart / dashboard pattern: load the planned payload from
            // ExecutedPlans by planId, diff against the live record, return
            // concrete discrepancies. Tester's preferred path now that
            // UIBuilder's plan_create_form / plan_create_view stage payloads
            // server-side — the legacy verify_form_creation /
            // verify_view_creation tools above stay registered as fallbacks
            // for the rare case where a planId isn't available (e.g. an
            // artifact created outside this session).
            var verifyFormAgainstPlanTool = new VerifyFormAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var verifyViewAgainstPlanTool = new VerifyViewAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
            var fullValidationTool = new RunFullValidationTool(dataverseClient, projectStateStore, sessionContext);
            // Read-only orientation tools so the Tester can inspect actual state without
            // relying solely on plan-keyed lookups.
            var tablesListTool = new GetTablesListTool(dataverseClient, sp.GetRequiredService<ILogger<GetTablesListTool>>());
            var tableDetailsTool = new GetTableDetailsTool(dataverseClient);
            // Required for verifying ExecuteGenerateTestData runs — counting records is
            // the right post-data-population check, NOT a schema verifier (which would
            // false-FAIL on default-vs-actual mismatches like HasNotes).
            var countRecordsTool = new CountRecordsTool(dataverseClient);
            // Sample-row fetch for content verification — count alone doesn't catch
            // empty-string or wrong-value populations.
            var testerGetRecordsTool = new GetRecordsTool(dataverseClient);
            var testerRunFetchXmlTool = new RunFetchXmlTool(dataverseClient);
            var testerRunSavedQueryTool = new RunSavedQueryTool(dataverseClient);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(verifyTableAgainstPlanTool.VerifyTableAgainstPlanAsync),
                AIFunctionFactory.Create(verifyFieldAgainstPlanTool.VerifyFieldAgainstPlanAsync),
                AIFunctionFactory.Create(verifyOptionSetAgainstPlanTool.VerifyGlobalOptionSetAgainstPlanAsync),
                AIFunctionFactory.Create(verifyChartAgainstPlanTool.VerifyChartAgainstPlanAsync),
                AIFunctionFactory.Create(getChartsListToolForTester.GetChartsListAsync),
                AIFunctionFactory.Create(verifyDashboardAgainstPlanTool.VerifyDashboardAgainstPlanAsync),
                AIFunctionFactory.Create(getDashboardsListToolForTester.GetDashboardsListAsync),
                AIFunctionFactory.Create(getDashboardDefinitionToolForTester.GetDashboardDefinitionAsync),
                AIFunctionFactory.Create(verifyAppAgainstPlanTool.VerifyAppAgainstPlanAsync),
                AIFunctionFactory.Create(getAppsListToolForTester.GetAppsListAsync),
                AIFunctionFactory.Create(getAppDetailsToolForTester.GetAppDetailsAsync),
                AIFunctionFactory.Create(validateAppToolForTester.ValidateAppAsync),
                AIFunctionFactory.Create(verifySiteMapAgainstPlanTool.VerifySiteMapAgainstPlanAsync),
                AIFunctionFactory.Create(getSiteMapForAppToolForTester.GetSiteMapForAppAsync),
                AIFunctionFactory.Create(getAppComponentsToolForTester.GetAppComponentsAsync),
                AIFunctionFactory.Create(verifyAppRoleAssignmentTool.VerifyAppRoleAssignmentAsync),
                AIFunctionFactory.Create(listAppRolesToolForTester.ListAppRolesAsync),
                AIFunctionFactory.Create(verifySecurityRoleAgainstPlanTool.VerifySecurityRoleAgainstPlanAsync),
                AIFunctionFactory.Create(verifyBulkPrivilegeGrantAgainstPlanTool.VerifyBulkPrivilegeGrantAgainstPlanAsync),
                AIFunctionFactory.Create(verifyWorkflowAgainstPlanTool.VerifyWorkflowAgainstPlanAsync),
                AIFunctionFactory.Create(verifyTableTool.VerifyTableCreationAsync),
                AIFunctionFactory.Create(verifyFieldTool.VerifyFieldCreationAsync),
                AIFunctionFactory.Create(verifyRelTool.VerifyRelationshipCreationAsync),
                AIFunctionFactory.Create(verifyFormTool.VerifyFormCreationAsync),
                AIFunctionFactory.Create(verifyViewTool.VerifyViewCreationAsync),
                AIFunctionFactory.Create(verifyFormAgainstPlanTool.VerifyFormAgainstPlanAsync),
                AIFunctionFactory.Create(verifyViewAgainstPlanTool.VerifyViewAgainstPlanAsync),
                AIFunctionFactory.Create(fullValidationTool.RunFullValidationAsync),
                AIFunctionFactory.Create(tablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(tableDetailsTool.GetTableDetailsAsync),
                AIFunctionFactory.Create(countRecordsTool.CountRecordsAsync),
                AIFunctionFactory.Create(testerGetRecordsTool.GetRecordsAsync),
                AIFunctionFactory.Create(testerRunFetchXmlTool.RunFetchXmlAsync),
                AIFunctionFactory.Create(testerRunSavedQueryTool.RunSavedQueryAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new TesterAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.Tester),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<TesterAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<TesterAgent>());

        return services;
    }
}
