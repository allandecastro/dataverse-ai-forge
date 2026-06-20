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
    public static IServiceCollection AddSolutionArchitect(this IServiceCollection services)
    {
        services.AddScoped<SolutionArchitectAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();

            var envInfoTool = new GetEnvironmentInfoTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<GetEnvironmentInfoTool>>());
            var solutionsListTool = new GetSolutionsListTool(dataverseClient, sp.GetRequiredService<ILogger<GetSolutionsListTool>>());
            var createSolutionTool = new CreateSolutionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<CreateSolutionTool>>());
            var selectSolutionTool = new SelectSolutionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<SelectSolutionTool>>());
            var updateSolutionVersionTool = new UpdateSolutionVersionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<UpdateSolutionVersionTool>>());
            var addExistingTableTool = new AddExistingTableToSolutionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<AddExistingTableToSolutionTool>>());
            var addExistingComponentTool = new AddExistingComponentToSolutionTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<AddExistingComponentToSolutionTool>>());
            var publicBaseUrl = sp.GetRequiredService<IConfiguration>()["Forge:PublicBaseUrl"]
                ?? "http://localhost:5000";
            // F-43: export path provider goes through ForgePaths so the
            // operator can mount a volume via DVFORGE_DATA_DIR. In dev, falls
            // back to {AppContext.BaseDirectory}/data/exports.
            var exportSolutionTool = new ExportSolutionTool(dataverseClient, projectStateStore, sessionContext, publicBaseUrl,
                () => ForgePaths.GetSubdir("exports"),
                sp.GetRequiredService<ILogger<ExportSolutionTool>>());
            var publishAllTool = new PublishAllTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<PublishAllTool>>());
            var orgResourcesTool = new GetOrganizationResourcesTool(dataverseClient, sp.GetRequiredService<ILogger<GetOrganizationResourcesTool>>());
            var handoffToBaTool = new HandoffToBusinessAnalystTool(projectStateStore, sessionContext);
            var handoffToBuilderTool = new HandoffToDataModelBuilderTool(projectStateStore, sessionContext);
            var publishersTool = new GetPublishersTool(dataverseClient, sp.GetRequiredService<ILogger<GetPublishersTool>>());
            var createPublisherTool = new CreatePublisherTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<CreatePublisherTool>>());
            var tablesListTool = new GetTablesListTool(dataverseClient, sp.GetRequiredService<ILogger<GetTablesListTool>>());
            var tableDetailsTool = new GetTableDetailsTool(dataverseClient);
            var formsListTool = new GetFormsListTool(dataverseClient);
            var viewsListTool = new GetViewsListTool(dataverseClient);
            var validateSchemaTool = new ValidateSchemaNameTool();
            var checkOobTool = new CheckOobTableCoverageTool(dataverseClient);
            var saFieldOptionsTool = new GetFieldOptionsTool(dataverseClient, sp.GetRequiredService<ILogger<GetFieldOptionsTool>>());
            // Read-only record fetch — useful for "show me 5 contacts" investigation and
            // for resolving lookup-target GUIDs without bouncing back to the user.
            var saGetRecordsTool = new GetRecordsTool(dataverseClient);
            var saRunFetchXmlTool = new RunFetchXmlTool(dataverseClient);
            var saRunSavedQueryTool = new RunSavedQueryTool(dataverseClient);
            // Count-only — SA needs this so "how many records are on X?" doesn't
            // degrade to get_records(top=1) + "at least 1 record" theatre.
            var saCountRecordsTool = new CountRecordsTool(dataverseClient);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);
            var handoffToUIBuilderTool = new HandoffToUIBuilderTool(projectStateStore, sessionContext);
            var handoffToDataPopulationTool = new HandoffToDataPopulationTool(projectStateStore, sessionContext);
            var handoffToSecurityBuilderTool = new HandoffToSecurityBuilderTool(projectStateStore, sessionContext);
            var handoffToAutomationBuilderTool = new HandoffToAutomationBuilderTool(projectStateStore, sessionContext);
            var undoLastOperationTool = new UndoLastOperationTool(dataverseClient, projectStateStore, sessionContext);
            // Model-driven app read tools — Phase 1 of MDA management. SA can list /
            // inspect apps but doesn't write; writes are routed to UIBuilder once the
            // create/update/delete tools land in later phases.
            var saGetAppsListTool = new GetAppsListTool(dataverseClient);
            var saGetAppDetailsTool = new GetAppDetailsTool(dataverseClient);
            var saGetAppFullTool = new GetAppFullTool(dataverseClient);
            var saGetSiteMapForAppTool = new GetSiteMapForAppTool(dataverseClient);
            var saListAppRolesTool = new ListAppRolesTool(dataverseClient);
            // Phase 5 reads — security roles. Shared with UIBuilder; SA can answer
            // "what roles exist?" / "what's the id of System Customizer?" without
            // bouncing through UIBuilder. Writes (assign/unassign) stay UIB-only.
            var saGetSecurityRolesTool = new GetSecurityRolesTool(dataverseClient, sp.GetRequiredService<ILogger<GetSecurityRolesTool>>());
            var saGetSecurityRoleDetailsTool = new GetSecurityRoleDetailsTool(dataverseClient);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(envInfoTool.GetEnvironmentInfoAsync),
                AIFunctionFactory.Create(solutionsListTool.GetSolutionsListAsync),
                AIFunctionFactory.Create(createSolutionTool.CreateSolutionAsync),
                AIFunctionFactory.Create(selectSolutionTool.SelectSolutionAsync),
                AIFunctionFactory.Create(updateSolutionVersionTool.UpdateSolutionVersionAsync),
                AIFunctionFactory.Create(addExistingTableTool.AddExistingTableToSolutionAsync),
                AIFunctionFactory.Create(addExistingComponentTool.AddExistingComponentToSolutionAsync),
                AIFunctionFactory.Create(exportSolutionTool.ExportSolutionAsync),
                AIFunctionFactory.Create(publishAllTool.PublishAllAsync),
                AIFunctionFactory.Create(orgResourcesTool.GetOrganizationResourcesAsync),
                AIFunctionFactory.Create(handoffToBaTool.HandoffToBusinessAnalystAsync),
                AIFunctionFactory.Create(handoffToBuilderTool.HandoffToDataModelBuilderAsync),
                AIFunctionFactory.Create(publishersTool.GetPublishersAsync),
                AIFunctionFactory.Create(createPublisherTool.CreatePublisherAsync),
                AIFunctionFactory.Create(tablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(tableDetailsTool.GetTableDetailsAsync),
                AIFunctionFactory.Create(saGetRecordsTool.GetRecordsAsync),
                AIFunctionFactory.Create(saRunFetchXmlTool.RunFetchXmlAsync),
                AIFunctionFactory.Create(saRunSavedQueryTool.RunSavedQueryAsync),
                AIFunctionFactory.Create(saCountRecordsTool.CountRecordsAsync),
                AIFunctionFactory.Create(formsListTool.GetFormsListAsync),
                AIFunctionFactory.Create(viewsListTool.GetViewsListAsync),
                AIFunctionFactory.Create(validateSchemaTool.ValidateSchemaNameAsync),
                AIFunctionFactory.Create(checkOobTool.CheckOobTableCoverageAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                AIFunctionFactory.Create(handoffToUIBuilderTool.HandoffToUIBuilderAsync),
                AIFunctionFactory.Create(handoffToDataPopulationTool.HandoffToDataPopulationAsync),
                AIFunctionFactory.Create(undoLastOperationTool.UndoLastOperationAsync),
                AIFunctionFactory.Create(undoLastOperationTool.ListUndoableOperationsAsync),
                AIFunctionFactory.Create(saGetAppsListTool.GetAppsListAsync),
                AIFunctionFactory.Create(saGetAppDetailsTool.GetAppDetailsAsync),
                AIFunctionFactory.Create(saGetAppFullTool.GetAppFullAsync),
                AIFunctionFactory.Create(saGetSiteMapForAppTool.GetSiteMapForAppAsync),
                AIFunctionFactory.Create(saListAppRolesTool.ListAppRolesAsync),
                AIFunctionFactory.Create(saGetSecurityRolesTool.GetSecurityRolesAsync),
                AIFunctionFactory.Create(saGetSecurityRoleDetailsTool.GetSecurityRoleDetailsAsync),
                AIFunctionFactory.Create(handoffToSecurityBuilderTool.HandoffToSecurityBuilderAsync),
                AIFunctionFactory.Create(handoffToAutomationBuilderTool.HandoffToAutomationBuilderAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.BusinessAnalyst, AgentNames.DataModelBuilder, AgentNames.UIBuilder, AgentNames.DataPopulation, AgentNames.Tester, AgentNames.SecurityBuilder, AgentNames.AutomationBuilder }.AsReadOnly();

            return new SolutionArchitectAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.SolutionArchitect),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<SolutionArchitectAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<SolutionArchitectAgent>());

        return services;
    }
}
