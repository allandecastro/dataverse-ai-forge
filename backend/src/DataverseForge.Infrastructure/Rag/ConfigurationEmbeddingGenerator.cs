using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Embedding generator that follows the live configuration. The settings it depends on
/// (<c>AzureAI:Endpoint</c>, <c>AzureAI:ApiKey</c>, <c>Rag:EmbeddingProvider</c>,
/// <c>Rag:EmbeddingDeployment</c>, <c>Rag:GitHubPersonalAccessToken</c>) can change at
/// runtime through Settings → Save (user-settings.json is reload-on-change), so they are
/// re-read on every call and the inner SDK client is rebuilt only when one of them changed.
/// A plain singleton client would keep the values seen at first use until a restart.
/// </summary>
internal sealed class ConfigurationEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    internal const string DefaultDeployment = "text-embedding-3-small";

    private readonly IConfiguration _configuration;
    private readonly Func<EmbeddingSettings, IEmbeddingGenerator<string, Embedding<float>>> _factory;
    private readonly object _lock = new();
    private EmbeddingSettings? _current;
    private IEmbeddingGenerator<string, Embedding<float>>? _inner;
    private bool _disposed;

    public ConfigurationEmbeddingGenerator(IConfiguration configuration)
        : this(configuration, CreateClient)
    {
    }

    internal ConfigurationEmbeddingGenerator(
        IConfiguration configuration,
        Func<EmbeddingSettings, IEmbeddingGenerator<string, Embedding<float>>> factory)
    {
        _configuration = configuration;
        _factory = factory;
    }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetInner().GenerateAsync(values, options, cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : GetInner().GetService(serviceType, serviceKey);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _inner?.Dispose();
            _inner = null;
            _disposed = true;
        }
    }

    internal static EmbeddingSettings Read(IConfiguration configuration)
    {
        static string Get(IConfiguration c, string key) => c[key]?.Trim() ?? string.Empty;

        var deployment = Get(configuration, "Rag:EmbeddingDeployment");
        var provider = Get(configuration, "Rag:EmbeddingProvider");
        return new EmbeddingSettings(
            Provider: provider.Length > 0 ? provider : "AzureOpenAI",
            Deployment: deployment.Length > 0 ? deployment : DefaultDeployment,
            AzureEndpoint: Get(configuration, "AzureAI:Endpoint"),
            AzureApiKey: Get(configuration, "AzureAI:ApiKey"),
            GitHubToken: Get(configuration, "Rag:GitHubPersonalAccessToken"));
    }

    private IEmbeddingGenerator<string, Embedding<float>> GetInner()
    {
        var settings = Read(_configuration);
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_inner is null || settings != _current)
            {
                _inner?.Dispose();
                _inner = _factory(settings);
                _current = settings;
            }
            return _inner;
        }
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateClient(EmbeddingSettings s) =>
        s.Provider.ToLowerInvariant() switch
        {
            // GitHub Models: same OpenAI-compatible API, different endpoint + a GitHub PAT.
            "githubmodels" =>
                new OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(s.GitHubToken),
                    new OpenAIClientOptions { Endpoint = new Uri("https://models.inference.ai.azure.com") })
                .GetEmbeddingClient(s.Deployment)
                .AsIEmbeddingGenerator(),

            // "azureopenai" and "localonnx" (reserved, not implemented) both use Azure OpenAI.
            _ =>
                new Azure.AI.OpenAI.AzureOpenAIClient(
                    new Uri(s.AzureEndpoint.Length > 0 ? s.AzureEndpoint : "https://placeholder.invalid/"),
                    new Azure.AzureKeyCredential(s.AzureApiKey))
                .GetEmbeddingClient(s.Deployment)
                .AsIEmbeddingGenerator(),
        };
}

/// <summary>The configuration values an embedding client is built from.</summary>
internal sealed record EmbeddingSettings(
    string Provider,
    string Deployment,
    string AzureEndpoint,
    string AzureApiKey,
    string GitHubToken);
