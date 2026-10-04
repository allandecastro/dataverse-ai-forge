using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using DataverseForge.Infrastructure.AzureAI;
using DataverseForge.Infrastructure.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI;

namespace DataverseForge.Infrastructure;

public static partial class DependencyInjection
{
    /// <summary>
    /// Registers all RAG infrastructure: embedding generator, SQLite store, document sources,
    /// and the two agent tools (SearchContext + IngestDocuments).
    ///
    /// <para><b>Embedding provider</b> — controlled by <c>Rag:EmbeddingProvider</c> in configuration:</para>
    /// <list type="bullet">
    ///   <item><c>"AzureOpenAI"</c> — uses <c>AzureAI:Endpoint</c> + <c>AzureAI:ApiKey</c>.
    ///         Deployment name from <c>Rag:EmbeddingDeployment</c>
    ///         (default <c>text-embedding-3-small</c>).</item>
    ///   <item><c>"GitHubModels"</c> — uses <c>https://models.inference.ai.azure.com</c> with
    ///         <c>Rag:GitHubPersonalAccessToken</c>. Same OpenAI-compatible API; no code change
    ///         required vs AzureOpenAI — only configuration differs. This is the target path
    ///         once the project migrates fully to GitHub Copilot as the AI backbone.</item>
    ///   <item><c>"LocalOnnx"</c> — reserved; falls back to AzureOpenAI until implemented.</item>
    /// </list>
    ///
    /// <para><b>Document sources</b> registered by default:
    /// <list type="bullet">
    ///   <item><see cref="ForgeExportsDocumentSource"/> — priority 0 (highest), always registered.</item>
    ///   <item><see cref="AzureDevOpsDocumentSource"/> — registered only when AzDo settings are present.</item>
    /// </list>
    /// Filesystem sources are registered dynamically at ingest time via <see cref="IngestDocumentsTool"/>.
    /// </para>
    /// </summary>
    internal const string DefaultEmbeddingDeployment = "text-embedding-3-small";

    public static IServiceCollection AddRag(this IServiceCollection services, IConfiguration configuration)
    {
        // ── SQLite RAG store — one database per project ────────────────────
        // rag/projects/{projectId}/rag.db. Scoped because the active project comes from the
        // request's ISessionContext; RagService and the tools are therefore scoped/transient too.
        services.AddSingleton(_ => new RagStoreProvider(
            Path.Combine(ForgePaths.GetSubdir("rag"), "projects")));
        services.AddScoped<IRagStore, ProjectScopedRagStore>();

        // ── Embedding generator ────────────────────────────────────────────
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var provider = configuration["Rag:EmbeddingProvider"]?.Trim() ?? "AzureOpenAI";
            // Deployment/model name differs per Azure resource — a hardcoded name 404s when
            // the user's embedding deployment is named anything else.
            var embeddingModel = configuration["Rag:EmbeddingDeployment"]?.Trim() is { Length: > 0 } configured
                ? configured
                : DefaultEmbeddingDeployment;

            return provider.ToLowerInvariant() switch
            {
                "githubmodels" =>
                    // GitHub Models uses the same OpenAI-compatible API.
                    // Endpoint: https://models.inference.ai.azure.com
                    // Auth: GitHub PAT in Rag:GitHubPersonalAccessToken
                    // To switch from AzureOpenAI to GitHub Models:
                    //   1. Set Rag:EmbeddingProvider = "GitHubModels"
                    //   2. Set Rag:GitHubPersonalAccessToken = <your GitHub PAT>
                    //   3. No code changes needed — same OpenAI client, different endpoint + token.
                    new OpenAIClient(
                        new System.ClientModel.ApiKeyCredential(configuration["Rag:GitHubPersonalAccessToken"] ?? string.Empty),
                        new OpenAIClientOptions { Endpoint = new Uri("https://models.inference.ai.azure.com") })
                    .GetEmbeddingClient(embeddingModel)
                    .AsIEmbeddingGenerator(),

                // "azureopenai" and "localonnx" (fallback) both use Azure OpenAI for now
                _ =>
                    new Azure.AI.OpenAI.AzureOpenAIClient(
                        new Uri(configuration["AzureAI:Endpoint"] ?? "https://placeholder.invalid/"),
                        new Azure.AzureKeyCredential(configuration["AzureAI:ApiKey"] ?? string.Empty))
                    .GetEmbeddingClient(embeddingModel)
                    .AsIEmbeddingGenerator()
            };
        });

        // ── Document sources ───────────────────────────────────────────────
        services.AddSingleton<ForgeExportsDocumentSource>();
        services.AddSingleton<IDocumentSource>(sp => sp.GetRequiredService<ForgeExportsDocumentSource>());

        // AzDo source: registered only when credentials are configured
        var azdoOrgUrl = configuration["AzureDevOps:OrganizationUrl"];
        var azdoProject = configuration["AzureDevOps:Project"];
        var azdoPat = configuration["AzureDevOps:PersonalAccessToken"];
        if (!string.IsNullOrWhiteSpace(azdoOrgUrl) &&
            !string.IsNullOrWhiteSpace(azdoProject) &&
            !string.IsNullOrWhiteSpace(azdoPat))
        {
            services.AddSingleton<IDocumentSource>(sp => new AzureDevOpsDocumentSource(
                azdoOrgUrl, azdoProject, azdoPat,
                sp.GetRequiredService<ILogger<AzureDevOpsDocumentSource>>()));
        }

        // ── RagService ─────────────────────────────────────────────────────
        services.AddScoped<RagService>(sp =>
        {
            var store = sp.GetRequiredService<IRagStore>();
            var embedding = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
            var sources = sp.GetServices<IDocumentSource>();
            var logger = sp.GetRequiredService<ILogger<RagService>>();
            return new RagService(store, embedding, sources, logger);
        });

        // ── LLM analysis service (used by CompareSourcesTool + SearchConflictTool) ──
        // Pass a Func<IChatClient> so that GetClientForAgent is called fresh on every LLM
        // invocation. This means credential reconfiguration (Settings → Save) is reflected
        // immediately — the singleton registration does not bake in a stale client reference.
        services.AddSingleton<ILlmAnalysisService>(sp =>
        {
            var clientFactory = sp.GetRequiredService<ChatClientFactory>();
            return new LlmAnalysisService(
                () => clientFactory.GetClientForAgent(AgentNames.DocumentaryAgent),
                sp.GetRequiredService<ILogger<LlmAnalysisService>>());
        });

        // ── Document source factory (for runtime-created filesystem/git sources) ─────
        services.AddSingleton<IDocumentSourceFactory, DocumentSourceFactory>();

        // ── Tools (wired into agents in DependencyInjection.BusinessAnalyst.cs,
        //          DependencyInjection.SolutionArchitect.cs and
        //          DependencyInjection.DocumentaryAgent.cs) ──────────────────
        services.AddTransient<SearchContextTool>();
        services.AddTransient<CompareSourcesTool>(sp => new CompareSourcesTool(
            sp.GetRequiredService<RagService>(),
            sp.GetRequiredService<IRagStore>(),
            sp.GetRequiredService<ILlmAnalysisService>(),
            sp.GetRequiredService<ILogger<CompareSourcesTool>>()));
        services.AddTransient<ListSourcesTool>();
        services.AddTransient<DeleteSourceTool>();
        services.AddTransient<SearchConflictTool>(sp => new SearchConflictTool(
            sp.GetRequiredService<RagService>(),
            sp.GetRequiredService<IRagStore>(),
            sp.GetRequiredService<ILlmAnalysisService>(),
            sp.GetRequiredService<ILogger<SearchConflictTool>>()));
        services.AddTransient<IngestDocumentsTool>(sp => new IngestDocumentsTool(
            sp.GetRequiredService<IRagStore>(),
            sp.GetRequiredService<RagService>(),
            sp.GetServices<IDocumentSource>(),
            sp.GetRequiredService<IDocumentSourceFactory>(),
            sp.GetRequiredService<ILogger<IngestDocumentsTool>>()));

        return services;
    }
}

