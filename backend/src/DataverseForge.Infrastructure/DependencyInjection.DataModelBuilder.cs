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
    public static IServiceCollection AddDataModelBuilder(this IServiceCollection services)
    {
        services.AddScoped<DataModelBuilderAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var planCreateTableTool = new PlanCreateTableTool(projectStateStore, sessionContext);
            var executeCreateTableTool = new ExecuteCreateTableTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateTableTool>>());
            var planModifyTableTool = new PlanModifyTableTool(projectStateStore, sessionContext, dataverseClient);
            var executeModifyTableTool = new ExecuteModifyTableTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteModifyTableTool>>());
            var planCreateFieldTool = new PlanCreateFieldTool(projectStateStore, sessionContext, dataverseClient);
            var executeCreateFieldTool = new ExecuteCreateFieldTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateFieldTool>>());
            var planCreateRelTool = new PlanCreateOneToManyRelationshipTool(dataverseClient, projectStateStore, sessionContext);
            var executeCreateRelTool = new ExecuteCreateOneToManyRelationshipTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateOneToManyRelationshipTool>>());
            var planCreateM2MTool = new PlanCreateManyToManyRelationshipTool(projectStateStore, sessionContext, dataverseClient);
            var executeCreateM2MTool = new ExecuteCreateManyToManyRelationshipTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateManyToManyRelationshipTool>>());
            var planCreatePolyTool = new PlanCreatePolymorphicLookupTool(projectStateStore, sessionContext, dataverseClient);
            var executeCreatePolyTool = new ExecuteCreatePolymorphicLookupTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreatePolymorphicLookupTool>>());
            var planCreateCustomerTool = new PlanCreateCustomerLookupTool(projectStateStore, sessionContext, dataverseClient);
            var executeCreateCustomerTool = new ExecuteCreateCustomerLookupTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateCustomerLookupTool>>());
            var planCreateGlobalOsTool = new PlanCreateGlobalOptionSetTool(projectStateStore, sessionContext, dataverseClient);
            var executeCreateGlobalOsTool = new ExecuteCreateGlobalOptionSetTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateGlobalOptionSetTool>>());
            var getGlobalOptionSetsTool = new GetGlobalOptionSetsTool(dataverseClient);
            var getFieldOptionsTool = new GetFieldOptionsTool(dataverseClient, sp.GetRequiredService<ILogger<GetFieldOptionsTool>>());
            var planAddOptionTool = new PlanAddOptionToOptionSetTool(projectStateStore, sessionContext, dataverseClient);
            var executeAddOptionTool = new ExecuteAddOptionToOptionSetTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteAddOptionToOptionSetTool>>());
            var planAddStatusOptionTool = new PlanAddStatusOptionTool(projectStateStore, sessionContext, dataverseClient);
            var executeAddStatusOptionTool = new ExecuteAddStatusOptionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteAddStatusOptionTool>>());
            var planUpdateOptionTool = new PlanUpdateOptionTool(projectStateStore, sessionContext);
            var executeUpdateOptionTool = new ExecuteUpdateOptionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteUpdateOptionTool>>());
            var planCreateAltKeyTool = new PlanCreateAlternateKeyTool(projectStateStore, sessionContext, dataverseClient);
            var executeCreateAltKeyTool = new ExecuteCreateAlternateKeyTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteCreateAlternateKeyTool>>());
            var deleteAltKeyTool = new DeleteAlternateKeyTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteAlternateKeyTool>>());
            var deactivateAltKeyTool = new DeactivateAlternateKeyTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeactivateAlternateKeyTool>>());
            var reactivateAltKeyTool = new ReactivateAlternateKeyTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ReactivateAlternateKeyTool>>());
            var planModifyFieldTool = new PlanModifyFieldTool(projectStateStore, sessionContext);
            var executeModifyFieldTool = new ExecuteModifyFieldTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteModifyFieldTool>>());
            var deleteFieldTool = new DeleteFieldTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DeleteFieldTool>>());
            var removeFieldFromSolutionTool = new RemoveFieldFromSolutionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<RemoveFieldFromSolutionTool>>());
            var requestApprovalTool = new RequestApprovalTool(projectStateStore, sessionContext);
            var requestBatchApprovalTool = new RequestBatchApprovalTool();
            var discardPendingPlanTool = new DiscardPendingPlanTool(projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DiscardPendingPlanTool>>());
            var listPendingPlansTool = new ListPendingPlansTool(projectStateStore, sessionContext);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(requestApprovalTool.RequestApprovalAsync),
                AIFunctionFactory.Create(requestBatchApprovalTool.RequestBatchApprovalAsync),
                AIFunctionFactory.Create(discardPendingPlanTool.DiscardPendingPlanAsync),
                AIFunctionFactory.Create(listPendingPlansTool.ListPendingPlansAsync),
                AIFunctionFactory.Create(planCreateTableTool.PlanCreateTableAsync),
                AIFunctionFactory.Create(executeCreateTableTool.ExecuteCreateTableAsync),
                AIFunctionFactory.Create(planModifyTableTool.PlanModifyTableAsync),
                AIFunctionFactory.Create(executeModifyTableTool.ExecuteModifyTableAsync),
                AIFunctionFactory.Create(planCreateFieldTool.PlanCreateFieldAsync),
                AIFunctionFactory.Create(executeCreateFieldTool.ExecuteCreateFieldAsync),
                AIFunctionFactory.Create(planCreateRelTool.PlanCreateOneToManyRelationshipAsync),
                AIFunctionFactory.Create(executeCreateRelTool.ExecuteCreateOneToManyRelationshipAsync),
                AIFunctionFactory.Create(planCreateM2MTool.PlanCreateManyToManyRelationshipAsync),
                AIFunctionFactory.Create(executeCreateM2MTool.ExecuteCreateManyToManyRelationshipAsync),
                AIFunctionFactory.Create(planCreatePolyTool.PlanCreatePolymorphicLookupAsync),
                AIFunctionFactory.Create(executeCreatePolyTool.ExecuteCreatePolymorphicLookupAsync),
                AIFunctionFactory.Create(planCreateCustomerTool.PlanCreateCustomerLookupAsync),
                AIFunctionFactory.Create(executeCreateCustomerTool.ExecuteCreateCustomerLookupAsync),
                AIFunctionFactory.Create(getGlobalOptionSetsTool.GetGlobalOptionSetsAsync),
                AIFunctionFactory.Create(getFieldOptionsTool.GetFieldOptionsAsync),
                AIFunctionFactory.Create(planCreateGlobalOsTool.PlanCreateGlobalOptionSetAsync),
                AIFunctionFactory.Create(executeCreateGlobalOsTool.ExecuteCreateGlobalOptionSetAsync),
                AIFunctionFactory.Create(planAddOptionTool.PlanAddOptionToOptionSetAsync),
                AIFunctionFactory.Create(executeAddOptionTool.ExecuteAddOptionToOptionSetAsync),
                AIFunctionFactory.Create(planAddStatusOptionTool.PlanAddStatusOptionAsync),
                AIFunctionFactory.Create(executeAddStatusOptionTool.ExecuteAddStatusOptionAsync),
                AIFunctionFactory.Create(planUpdateOptionTool.PlanUpdateOptionAsync),
                AIFunctionFactory.Create(executeUpdateOptionTool.ExecuteUpdateOptionAsync),
                AIFunctionFactory.Create(planCreateAltKeyTool.PlanCreateAlternateKeyAsync),
                AIFunctionFactory.Create(executeCreateAltKeyTool.ExecuteCreateAlternateKeyAsync),
                AIFunctionFactory.Create(deleteAltKeyTool.DeleteAlternateKeyAsync),
                AIFunctionFactory.Create(deactivateAltKeyTool.DeactivateAlternateKeyAsync),
                AIFunctionFactory.Create(reactivateAltKeyTool.ReactivateAlternateKeyAsync),
                AIFunctionFactory.Create(planModifyFieldTool.PlanModifyFieldAsync),
                AIFunctionFactory.Create(executeModifyFieldTool.ExecuteModifyFieldAsync),
                AIFunctionFactory.Create(deleteFieldTool.DeleteFieldAsync),
                AIFunctionFactory.Create(removeFieldFromSolutionTool.RemoveFieldFromSolutionAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Tester }.AsReadOnly();

            return new DataModelBuilderAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.DataModelBuilder),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<DataModelBuilderAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<DataModelBuilderAgent>());

        return services;
    }
}
