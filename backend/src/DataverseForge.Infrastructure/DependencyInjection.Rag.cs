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
    ///   <item><see cref="AzureDevOpsDocumentSource"/> — not registered: built per project at ingest time.</item>
    /// </list>
    /// Filesystem sources are registered dynamically at ingest time via <see cref="IngestDocumentsTool"/>.
    /// </para>
    /// </summary>

    public static IServiceCollection AddRag(this IServiceCollection services, IConfiguration configuration)
    {
        // ── SQLite RAG store — one database per project ────────────────────
        // rag/projects/{projectId}/rag.db. Scoped because the active project comes from the
        // request's ISessionContext; RagService and the tools are therefore scoped/transient too.
        services.AddSingleton(_ => new RagStoreProvider(
            Path.Combine(ForgePaths.GetSubdir("rag"), "projects")));
        services.AddScoped<IRagStore, ProjectScopedRagStore>();

        // ── Embedding generator ────────────────────────────────────────────
        // Re-reads Rag:* and AzureAI:* on every call, so a deployment name, endpoint or key
        // changed in Settings applies without a backend restart.
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            _ => new ConfigurationEmbeddingGenerator(configuration));

        // ── Document sources ───────────────────────────────────────────────
        services.AddSingleton<ForgeExportsDocumentSource>();
        services.AddSingleton<IDocumentSource>(sp => sp.GetRequiredService<ForgeExportsDocumentSource>());

        // The Azure DevOps source is per project (its credentials are the project's):
        // IngestDocumentsTool builds it on demand through IDocumentSourceFactory.

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
            sp.GetRequiredService<IProjectConnectionStore>(),
            sp.GetRequiredService<ISessionContext>(),
            sp.GetRequiredService<ILogger<IngestDocumentsTool>>()));

        return services;
    }
}

