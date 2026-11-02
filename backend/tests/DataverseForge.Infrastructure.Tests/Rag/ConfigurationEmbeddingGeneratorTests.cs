using DataverseForge.Infrastructure.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace DataverseForge.Infrastructure.Tests.Rag;

/// <summary>
/// The embedding generator must follow Settings → Save without a backend restart:
/// a changed deployment, endpoint or key rebuilds the inner client; unchanged settings reuse it.
/// </summary>
public sealed class ConfigurationEmbeddingGeneratorTests
{
    private sealed class FakeGenerator(EmbeddingSettings settings) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public EmbeddingSettings Settings { get; } = settings;
        public bool Disposed { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(new float[] { 1f }))));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => Disposed = true;
    }

    private static (IConfigurationRoot config, List<FakeGenerator> built, ConfigurationEmbeddingGenerator sut) Create(
        Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var built = new List<FakeGenerator>();
        var sut = new ConfigurationEmbeddingGenerator(config, s =>
        {
            var g = new FakeGenerator(s);
            built.Add(g);
            return g;
        });
        return (config, built, sut);
    }

    [Fact]
    public async Task Unset_deployment_falls_back_to_default()
    {
        var (_, built, sut) = Create(new() { ["AzureAI:Endpoint"] = "https://x.openai.azure.com/" });

        await sut.GenerateAsync(["a"]);

        Assert.Equal("text-embedding-3-small", Assert.Single(built).Settings.Deployment);
        Assert.Equal("AzureOpenAI", built[0].Settings.Provider);
    }

    [Fact]
    public async Task Unchanged_settings_reuse_the_client()
    {
        var (_, built, sut) = Create(new() { ["Rag:EmbeddingDeployment"] = "emb" });

        await sut.GenerateAsync(["a"]);
        await sut.GenerateAsync(["b"]);

        Assert.Single(built);
    }

    [Theory]
    [InlineData("Rag:EmbeddingDeployment", "my-embeddings")]
    [InlineData("AzureAI:Endpoint", "https://other.openai.azure.com/")]
    [InlineData("AzureAI:ApiKey", "new-key")]
    public async Task Changed_setting_rebuilds_and_disposes_the_old_client(string key, string value)
    {
        var (config, built, sut) = Create(new()
        {
            ["AzureAI:Endpoint"] = "https://x.openai.azure.com/",
            ["AzureAI:ApiKey"] = "key",
            ["Rag:EmbeddingDeployment"] = "emb",
        });
        await sut.GenerateAsync(["a"]);

        config[key] = value;
        await sut.GenerateAsync(["b"]);

        Assert.Equal(2, built.Count);
        Assert.True(built[0].Disposed);
        Assert.False(built[1].Disposed);
    }

    [Fact]
    public async Task Dispose_releases_the_client()
    {
        var (_, built, sut) = Create(new());
        await sut.GenerateAsync(["a"]);

        sut.Dispose();

        Assert.True(Assert.Single(built).Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => sut.GenerateAsync(["b"]));
    }
}
