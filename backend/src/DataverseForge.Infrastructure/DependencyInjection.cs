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
            var handoffToReviewerTool = new HandoffToReviewerTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);
            var handoffToUIBuilderTool = new HandoffToUIBuilderTool(projectStateStore, sessionContext);
            var handoffToDataPopulationTool = new HandoffToDataPopulationTool(projectStateStore, sessionContext);
            var undoLastOperationTool = new UndoLastOperationTool(dataverseClient, projectStateStore, sessionContext);

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
                AIFunctionFactory.Create(formsListTool.GetFormsListAsync),
                AIFunctionFactory.Create(viewsListTool.GetViewsListAsync),
                AIFunctionFactory.Create(validateSchemaTool.ValidateSchemaNameAsync),
                AIFunctionFactory.Create(checkOobTool.CheckOobTableCoverageAsync),
                AIFunctionFactory.Create(handoffToReviewerTool.HandoffToReviewerAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                AIFunctionFactory.Create(handoffToUIBuilderTool.HandoffToUIBuilderAsync),
                AIFunctionFactory.Create(handoffToDataPopulationTool.HandoffToDataPopulationAsync),
                AIFunctionFactory.Create(undoLastOperationTool.UndoLastOperationAsync),
                AIFunctionFactory.Create(undoLastOperationTool.ListUndoableOperationsAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.BusinessAnalyst, AgentNames.DataModelBuilder, AgentNames.UIBuilder, AgentNames.DataPopulation, AgentNames.Reviewer, AgentNames.Tester }.AsReadOnly();

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
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToReviewerTool = new HandoffToReviewerTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(requestApprovalTool.RequestApprovalAsync),
                AIFunctionFactory.Create(requestBatchApprovalTool.RequestBatchApprovalAsync),
                AIFunctionFactory.Create(discardPendingPlanTool.DiscardPendingPlanAsync),
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
                AIFunctionFactory.Create(handoffToReviewerTool.HandoffToReviewerAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester }.AsReadOnly();

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
            var requestApprovalTool = new RequestApprovalTool(projectStateStore, sessionContext);
            var discardPendingPlanTool = new DiscardPendingPlanTool(projectStateStore, sessionContext, sp.GetRequiredService<ILogger<DiscardPendingPlanTool>>());
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToReviewerTool = new HandoffToReviewerTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(getFormsListTool.GetFormsListAsync),
                AIFunctionFactory.Create(getViewsListTool.GetViewsListAsync),
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
                AIFunctionFactory.Create(requestApprovalTool.RequestApprovalAsync),
                AIFunctionFactory.Create(discardPendingPlanTool.DiscardPendingPlanAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                AIFunctionFactory.Create(handoffToReviewerTool.HandoffToReviewerAsync),
                AIFunctionFactory.Create(handoffToTesterTool.HandoffToTesterAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect, AgentNames.Reviewer, AgentNames.Tester }.AsReadOnly();

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

    public static IServiceCollection AddReviewer(this IServiceCollection services)
    {
        services.AddScoped<ReviewerAgent>(sp =>
        {
            var dataverseClient = sp.GetRequiredService<IDataverseClient>();
            var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
            var sessionContext = sp.GetRequiredService<ISessionContext>();
            var validateSchemaDesignTool = new ValidateSchemaDesignTool();
            var tablesListTool = new GetTablesListTool(dataverseClient, sp.GetRequiredService<ILogger<GetTablesListTool>>());
            var tableDetailsTool = new GetTableDetailsTool(dataverseClient);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(validateSchemaDesignTool.ValidateSchemaDesignAsync),
                AIFunctionFactory.Create(tablesListTool.GetTablesListAsync),
                AIFunctionFactory.Create(tableDetailsTool.GetTableDetailsAsync),
                AIFunctionFactory.Create(handoffToArchitectTool.HandoffToSolutionArchitectAsync),
                BuildSuggestRepliesTool()
            };

            var handoffTargets = new List<string> { AgentNames.SolutionArchitect }.AsReadOnly();

            return new ReviewerAgent(
                sp.GetRequiredService<ChatClientFactory>().GetClientForAgent(AgentNames.Reviewer),
                projectStateStore,
                sp.GetRequiredService<IConversationStore>(),
                sessionContext,
                tools,
                handoffTargets,
                sp.GetRequiredService<ILogger<ReviewerAgent>>());
        });

        services.AddScoped<IAgent>(sp => sp.GetRequiredService<ReviewerAgent>());

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
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(verifyTableAgainstPlanTool.VerifyTableAgainstPlanAsync),
                AIFunctionFactory.Create(verifyFieldAgainstPlanTool.VerifyFieldAgainstPlanAsync),
                AIFunctionFactory.Create(verifyOptionSetAgainstPlanTool.VerifyGlobalOptionSetAgainstPlanAsync),
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
            var planGenerateRefTool = new PlanGenerateReferenceDataTool(projectStateStore, sessionContext);
            var planGenerateTestTool = new PlanGenerateTestDataTool(projectStateStore, sessionContext, dataverseClient, sp.GetRequiredService<ILogger<PlanGenerateTestDataTool>>());
            var executeGenerateTestTool = new ExecuteGenerateTestDataTool(dataverseClient, projectStateStore, sessionContext, sp.GetRequiredService<ILogger<ExecuteGenerateTestDataTool>>());
            var countTool = new CountRecordsTool(dataverseClient);
            // Read-only record fetch — closes the gap that forced the agent to ask the user
            // for Contact GUIDs when populating lookup fields on Gift records.
            var dpGetRecordsTool = new GetRecordsTool(dataverseClient);
            var handoffToArchitectTool = new HandoffToSolutionArchitectTool(projectStateStore, sessionContext);
            var handoffToTesterTool = new HandoffToTesterTool(projectStateStore, sessionContext);

            var tools = new List<AITool>
            {
                AIFunctionFactory.Create(planPopulateTool.PlanPopulateReferenceDataAsync),
                AIFunctionFactory.Create(executePopulateTool.ExecutePopulateReferenceDataAsync),
                AIFunctionFactory.Create(planGenerateRefTool.PlanGenerateReferenceDataAsync),
                AIFunctionFactory.Create(planGenerateTestTool.PlanGenerateTestDataAsync),
                AIFunctionFactory.Create(executeGenerateTestTool.ExecuteGenerateTestDataAsync),
                AIFunctionFactory.Create(countTool.CountRecordsAsync),
                AIFunctionFactory.Create(dpGetRecordsTool.GetRecordsAsync),
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
