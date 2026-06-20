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
    public static IServiceCollection AddSecurityBuilder(this IServiceCollection services)
    {
        services.AddScoped<SecurityBuilderAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            // Phase 6 reads — security roles + BU + privilege metadata. SB owns
            // every read tool needed to plan a role; SA also has the role-list /
            // role-detail pair so it can answer "what roles exist" without
            // bouncing through SB. Plan tools resolve table+operation pairs to
            // real PrivilegeIds via get_privileges_for_table — keeps the agent
            // from inventing privilege GUIDs.
            var sbGetSecurityRolesTool = new GetSecurityRolesTool(dataverseClient, sp.GetRequiredService<ILogger<GetSecurityRolesTool>>());
            var sbGetSecurityRoleDetailsTool = new GetSecurityRoleDetailsTool(dataverseClient);
            var sbGetBusinessUnitsTool = new GetBusinessUnitsTool(dataverseClient);
            var sbGetRoleTemplatesTool = new GetRoleTemplatesTool(dataverseClient);
            var sbGetPrivilegesForTableTool = new GetPrivilegesForTableTool(dataverseClient, sp.GetRequiredService<ILogger<GetPrivilegesForTableTool>>());
            var sbGetRolePrivilegesTool = new GetRolePrivilegesTool(dataverseClient);
            // Tables-list helps SB resolve "give the Sales User read on Account" →
            // confirm 'account' is the correct logical name before calling
            // get_privileges_for_table.
            var sbTablesListTool = new GetTablesListTool(dataverseClient, sp.GetRequiredService<ILogger<GetTablesListTool>>());

            // Plan + Execute. Commit 1 shipped Create; Commit 2 adds Update;
            // Commit 3 adds Delete + undo wiring.
            var planCreateSecurityRoleTool = new PlanCreateSecurityRoleTool(projectStateStore, sessionContext, dataverseClient);
            var executeCreateSecurityRoleTool = new ExecuteCreateSecurityRoleTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateSecurityRoleTool>>());
            var planUpdateSecurityRoleTool = new PlanUpdateSecurityRoleTool(projectStateStore, sessionContext, dataverseClient);
            var executeUpdateSecurityRoleTool = new ExecuteUpdateSecurityRoleTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateSecurityRoleTool>>());
            var deleteSecurityRoleTool = new DeleteSecurityRoleTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteSecurityRoleTool>>());
            // Bulk privilege grant across N roles — same (privilege, depth) onto
            // a set of roles in one plan/execute pair. Avoids N separate update
            // plans for the common "give these three roles Read on Account" ask.
            var planGrantPrivilegeToRolesTool = new PlanGrantPrivilegeToRolesTool(projectStateStore, sessionContext, dataverseClient);
            var executeGrantPrivilegeToRolesTool = new ExecuteGrantPrivilegeToRolesTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteGrantPrivilegeToRolesTool>>());

            var requestApprovalTool = new RequestApprovalTool(projectStateStore, sessionContext);
            var discardPendingPlanTool = new DiscardPendingPlanTool(projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DiscardPendingPlanTool>>());
            var listPendingPlansTool = new ListPendingPlansTool(projectStateStore, sessionContext);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(sbGetSecurityRolesTool.GetSecurityRolesAsync),
                AIFunctionFactory.Create(sbGetSecurityRoleDetailsTool.GetSecurityRoleDetailsAsync),
                AIFunctionFactory.Create(sbGetBusinessUnitsTool.GetBusinessUnitsAsync),
                AIFunctionFactory.Create(sbGetRoleTemplatesTool.GetRoleTemplatesAsync),
                AIFunctionFactory.Create(sbGetPrivilegesForTableTool.GetPrivilegesForTableAsync),
                AIFunctionFactory.Create(sbGetRolePrivilegesTool.GetRolePrivilegesAsync),
                AIFunctionFactory.Create(sbTablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(planCreateSecurityRoleTool.PlanCreateSecurityRoleAsync),
                AIFunctionFactory.Create(executeCreateSecurityRoleTool.ExecuteCreateSecurityRoleAsync),
                AIFunctionFactory.Create(planUpdateSecurityRoleTool.PlanUpdateSecurityRoleAsync),
                AIFunctionFactory.Create(executeUpdateSecurityRoleTool.ExecuteUpdateSecurityRoleAsync),
                AIFunctionFactory.Create(deleteSecurityRoleTool.DeleteSecurityRoleAsync),
                AIFunctionFactory.Create(planGrantPrivilegeToRolesTool.PlanGrantPrivilegeToRolesAsync),
                AIFunctionFactory.Create(executeGrantPrivilegeToRolesTool.ExecuteGrantPrivilegeToRolesAsync),
                AIFunctionFactory.Create(requestApprovalTool.RequestApprovalAsync),
                AIFunctionFactory.Create(discardPendingPlanTool.DiscardPendingPlanAsync),
                AIFunctionFactory.Create(listPendingPlansTool.ListPendingPlansAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Tester }.AsReadOnly();

            return new SecurityBuilderAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.SecurityBuilder),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<SecurityBuilderAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<SecurityBuilderAgent>());

        return services;
    }
}
