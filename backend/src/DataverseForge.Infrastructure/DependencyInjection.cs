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

// Composition root. Shared infrastructure registrations live here; each agent's
// tool wiring lives in its own DependencyInjection.<Agent>.cs partial so this
// file doesn't grow without bound (one agent ≈ 50-180 lines of tool setup).
public static partial class DependencyInjection
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

    /// <summary>
    /// Binds <see cref="BearerTokenAuthOptions"/> from the <c>Auth</c> section.
    /// The middleware itself is registered in the pipeline in <c>Program.cs</c>
    /// via <c>app.UseMiddleware&lt;BearerTokenAuthMiddleware&gt;()</c>.
    ///
    /// Opt-in: leaving <c>Auth:SharedSecret</c> unset is the same as not
    /// registering the middleware at all (no enforcement). See
    /// <see cref="BearerTokenAuthMiddleware"/> for the threat-model and what
    /// IS / IS NOT gated.
    /// </summary>
    public static IServiceCollection AddBearerTokenAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<BearerTokenAuthOptions>(
            configuration.GetSection(BearerTokenAuthOptions.SectionName));
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

        // Register ChatClientFactory as singleton — supports lazy init + hot-reload.
        // Factory-method registration (vs. instance registration) so the container
        // owns the lifetime: ChatClientFactory implements IDisposable and DI will
        // call Dispose on app shutdown, releasing the underlying SDK pipelines
        // (HTTP connection pools, auth caches) rather than relying on GC. F-49.
        services.AddSingleton<ChatClientFactory>(sp =>
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
        // Single in-memory persistence path. Earlier versions branched on
        // `Persistence:Provider` to swap in a SQLite backend; that branch was
        // removed because we never deployed the persistent path in anger and
        // every test was hitting the in-memory store anyway. The signature
        // still takes IConfiguration so future callers can wire a real store
        // back in without touching every Program.cs.
        _ = configuration;
        services.AddSingleton<IProjectStateStore, InMemoryProjectStateStore>();
        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        return services;
    }

    public static IServiceCollection AddSessionContext(this IServiceCollection services)
    {
        services.AddScoped<ISessionContext, SessionContext>();
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

    public static IServiceCollection AddSettingsServices(this IServiceCollection services)
    {
        // Same protector the config layer uses to DECRYPT on load (Program.cs).
        // UserSettingsService uses it to ENCRYPT on write — both key off the
        // .secret.key file in the writable data dir, so they share one key.
        services.AddSingleton<ISecretProtector>(_ => AesGcmSecretProtector.ForDataDir());
        services.AddSingleton<UserSettingsService>();
        services.AddSingleton<SettingsTestService>();
        return services;
    }
}
