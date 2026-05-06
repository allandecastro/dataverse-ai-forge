using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Telemetry;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Infrastructure.AzureAI;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.AgUi;
using DataverseForge.Infrastructure.Settings;
using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DataverseForge.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Builds the SuggestReplies tool — registered with every agent so any of
    /// them can surface clickable reply chips after asking the user a question.
    /// The tool itself is a no-op; <c>FunctionCallEmitter</c> intercepts its
    /// arguments and emits a STATE_DELTA on /suggestedReplies for the frontend.
    /// </summary>
    private static AITool BuildSuggestRepliesTool() =>
        AIFunctionFactory.Create(new SuggestRepliesTool().SuggestRepliesAsync);

    /// <summary>
    /// Registers the XsdValidator as a singleton so its schema-set cache
    /// survives request scope. Loading XSDs is slow; caching is essential.
    /// </summary>
    public static IServiceCollection AddXsdValidator(this IServiceCollection services)
    {
        services.AddSingleton<XsdValidator>();
        return services;
    }


    public static IServiceCollection AddDataverseClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DataverseOptions>(
            configuration.GetSection(DataverseOptions.SectionName));

        services.AddTransient<DataverseAuthHandler>();
        services.AddTransient<DataverseRetryHandler>();
        services.AddTransient<DataverseLoggingHandler>();
        // Singleton credential cache — survives transient handler rotation by
        // IHttpClientFactory and lets the Sign-Out endpoint invalidate the
        // cached InteractiveBrowserCredential to force a fresh sign-in.
        services.AddSingleton<DataverseCredentialCache>();

        services.AddHttpClient<IDataverseClient, DataverseClient>((sp, client) =>
        {
            // Use a placeholder BaseAddress so HttpClient validation passes.
            // The DataverseAuthHandler rewrites the absolute URL using the
            // current EnvironmentUrl from IOptionsMonitor on each request.
            client.BaseAddress = new Uri("https://placeholder.invalid/");
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        // Pipeline order = registration order. The first added is OUTERMOST (closest
        // to caller); the last is INNERMOST (closest to network). LoggingHandler is
        // last so it sees the URL after AuthHandler rewrote it from placeholder, plus
        // the request body in its final form right before the wire.
        .AddHttpMessageHandler<DataverseRetryHandler>()
        .AddHttpMessageHandler<DataverseAuthHandler>()
        .AddHttpMessageHandler<DataverseLoggingHandler>();

        return services;
    }

    public static IServiceCollection AddAzureAiChatClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureAiOptions>(
            configuration.GetSection(AzureAiOptions.SectionName));

        // Register ChatClientFactory as singleton — supports lazy init + hot-reload
        services.AddSingleton(sp =>
        {
            var options = configuration
                .GetSection(AzureAiOptions.SectionName)
                .Get<AzureAiOptions>() ?? new AzureAiOptions();

            return new ChatClientFactory(options, sp.GetRequiredService<ILogger<ChatClientFactory>>());
        });

        return services;
    }

    public static IServiceCollection AddStores(this IServiceCollection services, IConfiguration configuration)
    {
        var persistenceOptions = configuration.GetSection(PersistenceOptions.SectionName).Get<PersistenceOptions>()
            ?? new PersistenceOptions();

        if (string.Equals(persistenceOptions.Provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<PersistenceOptions>(configuration.GetSection(PersistenceOptions.SectionName));
            services.AddSingleton<SqliteProjectStateStore>();
            services.AddSingleton<IProjectStateStore>(sp => sp.GetRequiredService<SqliteProjectStateStore>());
            services.AddSingleton<IConversationStore, SqliteConversationStore>();
        }
        else
        {
            services.AddSingleton<IProjectStateStore, InMemoryProjectStateStore>();
            services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        }

        return services;
    }

    public static IServiceCollection AddSessionContext(this IServiceCollection services)
    {
        services.AddScoped<ISessionContext, SessionContext>();
        return services;
    }

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
            var exportSolutionTool = new ExportSolutionTool(dataverseClient, projectStateStore, sessionContext, publicBaseUrl, sp.GetRequiredService<ILogger<ExportSolutionTool>>());
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
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);
            var handoffToUIBuilderTool = new HandoffToUIBuilderTool(projectStateStore, sessionContext);
            var handoffToDataPopulationTool = new HandoffToDataPopulationTool(projectStateStore, sessionContext);
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
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.BusinessAnalyst, AgentNames.DataModelBuilder, AgentNames.UIBuilder, AgentNames.DataPopulation, AgentNames.Tester }.AsReadOnly();

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

    public static IServiceCollection AddBusinessAnalyst(this IServiceCollection services)
    {
        services.AddScoped<BusinessAnalystAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            // Read-only tools so BA can investigate / clarify against live Dataverse state
            // before bouncing back to the Architect.
            var envInfoTool = new GetEnvironmentInfoTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<GetEnvironmentInfoTool>>());
            var solutionsListTool = new GetSolutionsListTool(dataverseClient, sp.GetRequiredService<ILogger<GetSolutionsListTool>>());
            var publishersTool = new GetPublishersTool(dataverseClient, sp.GetRequiredService<ILogger<GetPublishersTool>>());
            var tablesListTool = new GetTablesListTool(dataverseClient, sp.GetRequiredService<ILogger<GetTablesListTool>>());
            var tableDetailsTool = new GetTableDetailsTool(dataverseClient);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(envInfoTool.GetEnvironmentInfoAsync),
                AIFunctionFactory.Create(solutionsListTool.GetSolutionsListAsync),
                AIFunctionFactory.Create(publishersTool.GetPublishersAsync),
                AIFunctionFactory.Create(tablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(tableDetailsTool.GetTableDetailsAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new BusinessAnalystAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.BusinessAnalyst),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<BusinessAnalystAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<BusinessAnalystAgent>());

        return services;
    }

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
            var planCreateRelTool = new PlanCreateOneToManyRelationshipTool(projectStateStore, sessionContext);
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
            var planUpdateDashboardTool = new PlanUpdateDashboardTool(projectStateStore, sessionContext);
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
            var verifyAppAgainstPlanTool = new VerifyAppAgainstPlanTool(dataverseClient, projectStateStore, sessionContext);
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
            var verifyRelTool = new VerifyRelationshipCreationTool(dataverseClient);
            var verifyFormTool = new VerifyFormCreationTool(dataverseClient);
            var verifyViewTool = new VerifyViewCreationTool(dataverseClient);
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
                AIFunctionFactory.Create(verifyTableTool.VerifyTableCreationAsync),
                AIFunctionFactory.Create(verifyFieldTool.VerifyFieldCreationAsync),
                AIFunctionFactory.Create(verifyRelTool.VerifyRelationshipCreationAsync),
                AIFunctionFactory.Create(verifyFormTool.VerifyFormCreationAsync),
                AIFunctionFactory.Create(verifyViewTool.VerifyViewCreationAsync),
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

    public static IServiceCollection AddAgentOrchestrator(this IServiceCollection services)
    {
        services.AddScoped<AgentOrchestrator>();
        return services;
    }

    public static IServiceCollection AddAgUi(this IServiceCollection services)
    {
        services.AddSingleton<OrchestratingChatClient>(sp =>
        {
            var chatClientFactory = sp.GetRequiredService<ChatClientFactory>();
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var logger = sp.GetRequiredService<ILogger<OrchestratingChatClient>>();
            return new OrchestratingChatClient(scopeFactory, chatClientFactory, logger);
        });
        return services;
    }

    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(ForgeActivitySource.ServiceName, serviceVersion: ForgeActivitySource.ServiceVersion))
            .WithTracing(tracing => tracing
                .AddSource(ForgeActivitySource.ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint)))
            .WithMetrics(metrics => metrics
                .AddMeter(ForgeActivitySource.ServiceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddView("forge.dataverse.api.duration",
                    new ExplicitBucketHistogramConfiguration { Boundaries = ForgeActivitySource.LatencyBucketBoundaries })
                .AddView("forge.agent.turn.duration",
                    new ExplicitBucketHistogramConfiguration { Boundaries = ForgeActivitySource.LatencyBucketBoundaries })
                .AddView("forge.sse.stream.duration",
                    new ExplicitBucketHistogramConfiguration { Boundaries = ForgeActivitySource.LatencyBucketBoundaries })
                .AddView("forge.tool.execution.duration",
                    new ExplicitBucketHistogramConfiguration { Boundaries = ForgeActivitySource.LatencyBucketBoundaries })
                .AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint)));

        return services;
    }

    public static IServiceCollection AddSettingsServices(this IServiceCollection services)
    {
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<SettingsTestService>();
        return services;
    }
}
