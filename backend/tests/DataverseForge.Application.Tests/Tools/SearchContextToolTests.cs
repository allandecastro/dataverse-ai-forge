using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class SearchContextToolTests
{
    private static SearchContextTool BuildTool(IReadOnlyList<RagResult>? results = null)
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results ?? []);

        var embMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embMock.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>((inputs, _, _) =>
            {
                var embeddings = new GeneratedEmbeddings<Embedding<float>>(
                    inputs.Select(_ => new Embedding<float>(new float[3])).ToList());
                return Task.FromResult(embeddings);
            });

        var svc = new RagService(storeMock.Object, embMock.Object, [], NullLogger<RagService>.Instance);
        return new SearchContextTool(svc, NullLogger<SearchContextTool>.Instance);
    }

    [Fact]
    public async Task SearchContextAsync_NoResults_ReturnsHelpfulMessage()
    {
        var tool = BuildTool([]);
        var result = await tool.SearchContextAsync("what is the validation rule for montant?");
        Assert.Contains("No relevant context", result);
        Assert.Contains("ingest_documents", result);
    }

    [Fact]
    public async Task SearchContextAsync_WithResults_FormatsOutput()
    {
        var results = new[]
        {
            new RagResult("specs", "Specs validées", "/spec.md", 0, "Le montant ne peut pas être négatif.", 0.92f),
            new RagResult("transcripts", "Transcripts", "/meeting.vtt", 2, "On a dit que le montant doit être positif.", 0.81f)
        };

        var tool = BuildTool(results);
        var output = await tool.SearchContextAsync("validation du montant", topK: 2);

        Assert.Contains("[1]", output);
        Assert.Contains("Specs validées", output);
        Assert.Contains("Le montant ne peut pas être négatif", output);
        Assert.Contains("0.92", output);
        Assert.Contains("[2]", output);
        Assert.Contains("Transcripts", output);
    }

    [Fact]
    public async Task SearchContextAsync_EmptyQuery_ReturnsNoResults()
    {
        var tool = BuildTool([]);
        var result = await tool.SearchContextAsync(string.Empty);
        // Empty query → RagService returns empty → no results message
        Assert.Contains("No relevant context", result);
    }
}
