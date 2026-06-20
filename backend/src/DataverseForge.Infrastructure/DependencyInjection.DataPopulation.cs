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
    public static IServiceCollection AddDataPopulation(this IServiceCollection services)
    {
        services.AddScoped<DataPopulationAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var planPopulateTool = new PlanPopulateReferenceDataTool(projectStateStore, sessionContext);
            var executePopulateTool = new ExecutePopulateReferenceDataTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecutePopulateReferenceDataTool>>());
            var planGenerateTestTool = new PlanGenerateTestDataTool(projectStateStore, sessionContext, dataverseClient, sp.GetRequiredService<ILogger<PlanGenerateTestDataTool>>());
            var executeGenerateTestTool = new ExecuteGenerateTestDataTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteGenerateTestDataTool>>());
            var countTool = new CountRecordsTool(dataverseClient);
            // Read-only record fetch — closes the gap that forced the agent to ask the user
            // for Contact GUIDs when populating lookup fields on Gift records.
            var dpGetRecordsTool = new GetRecordsTool(dataverseClient);
            var dpRunFetchXmlTool = new RunFetchXmlTool(dataverseClient);
            var dpRunSavedQueryTool = new RunSavedQueryTool(dataverseClient);
            // Approval surface — DataPopulation creates records (a write to
            // Dataverse) and the prompt says "present plan for approval", but
            // the agent had no tool to actually surface an approval card. The
            // result was either text-only "is this ok?" prompts or silent
            // skip-and-execute. Now wired so the agent can request a real
            // approval card with risk levels, same as the other builders.
            var dpRequestApprovalTool = new RequestApprovalTool(projectStateStore, sessionContext);
            var dpRequestBatchApprovalTool = new RequestBatchApprovalTool();
            var dpDiscardPendingPlanTool = new DiscardPendingPlanTool(projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DiscardPendingPlanTool>>());
            var dpListPendingPlansTool = new ListPendingPlansTool(projectStateStore, sessionContext);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(planPopulateTool.PlanPopulateReferenceDataAsync),
                AIFunctionFactory.Create(executePopulateTool.ExecutePopulateReferenceDataAsync),
                AIFunctionFactory.Create(planGenerateTestTool.PlanGenerateTestDataAsync),
                AIFunctionFactory.Create(executeGenerateTestTool.ExecuteGenerateTestDataAsync),
                AIFunctionFactory.Create(countTool.CountRecordsAsync),
                AIFunctionFactory.Create(dpGetRecordsTool.GetRecordsAsync),
                AIFunctionFactory.Create(dpRunFetchXmlTool.RunFetchXmlAsync),
                AIFunctionFactory.Create(dpRunSavedQueryTool.RunSavedQueryAsync),
                AIFunctionFactory.Create(dpRequestApprovalTool.RequestApprovalAsync),
                AIFunctionFactory.Create(dpRequestBatchApprovalTool.RequestBatchApprovalAsync),
                AIFunctionFactory.Create(dpDiscardPendingPlanTool.DiscardPendingPlanAsync),
                AIFunctionFactory.Create(dpListPendingPlansTool.ListPendingPlansAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Tester }.AsReadOnly();

            return new DataPopulationAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.DataPopulation),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<DataPopulationAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<DataPopulationAgent>());

        return services;
    }
}
