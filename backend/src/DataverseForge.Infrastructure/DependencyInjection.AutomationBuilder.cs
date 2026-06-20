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
    public static IServiceCollection AddAutomationBuilder(this IServiceCollection services)
    {
        services.AddScoped<AutomationBuilderAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            // Phase 7 reads — workflows (BPF + Business Rule) + tables for the
            // plan-time logical-name + field validation.
            var abGetWorkflowsTool = new GetWorkflowsTool(dataverseClient);
            var abTablesListTool = new GetTablesListTool(dataverseClient, sp.GetRequiredService<ILogger<GetTablesListTool>>());
            var abTableDetailsTool = new GetTableDetailsTool(dataverseClient);

            // Phase 7 lifecycle tools — work for BOTH BPF + Business Rule since
            // they share the workflow entity. Commit D adds the Business Rule
            // CREATE plan/execute on top. BPF creation stays out of scope (XAML
            // designer in maker portal is the supported path).
            var abPlanUpdateWorkflowTool = new PlanUpdateWorkflowTool(projectStateStore, sessionContext, dataverseClient);
            var abExecuteUpdateWorkflowTool = new ExecuteUpdateWorkflowTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateWorkflowTool>>());
            var abActivateWorkflowTool = new ActivateWorkflowTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ActivateWorkflowTool>>());
            var abDeactivateWorkflowTool = new DeactivateWorkflowTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeactivateWorkflowTool>>());
            var abDeleteWorkflowTool = new DeleteWorkflowTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteWorkflowTool>>());

            var requestApprovalTool = new RequestApprovalTool(projectStateStore, sessionContext);
            var discardPendingPlanTool = new DiscardPendingPlanTool(projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DiscardPendingPlanTool>>());
            var listPendingPlansTool = new ListPendingPlansTool(projectStateStore, sessionContext);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(abGetWorkflowsTool.GetWorkflowsAsync),
                AIFunctionFactory.Create(abTablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(abTableDetailsTool.GetTableDetailsAsync),
                AIFunctionFactory.Create(abPlanUpdateWorkflowTool.PlanUpdateWorkflowAsync),
                AIFunctionFactory.Create(abExecuteUpdateWorkflowTool.ExecuteUpdateWorkflowAsync),
                AIFunctionFactory.Create(abActivateWorkflowTool.ActivateWorkflowAsync),
                AIFunctionFactory.Create(abDeactivateWorkflowTool.DeactivateWorkflowAsync),
                AIFunctionFactory.Create(abDeleteWorkflowTool.DeleteWorkflowAsync),
                AIFunctionFactory.Create(requestApprovalTool.RequestApprovalAsync),
                AIFunctionFactory.Create(discardPendingPlanTool.DiscardPendingPlanAsync),
                AIFunctionFactory.Create(listPendingPlansTool.ListPendingPlansAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Tester }.AsReadOnly();

            return new AutomationBuilderAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.AutomationBuilder),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<AutomationBuilderAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<AutomationBuilderAgent>());

        return services;
    }
}
