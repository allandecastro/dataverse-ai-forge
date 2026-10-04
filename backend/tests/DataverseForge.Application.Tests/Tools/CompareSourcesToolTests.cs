using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class CompareSourcesToolTests
{
    private static RagService BuildRagService(IRagStore store, float[] embeddingVector)
    {
        var embeddingMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embeddingMock.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>((inputs, _, _) =>
            {
                var results = new GeneratedEmbeddings<Embedding<float>>(
                    inputs.Select(_ => new Embedding<float>(embeddingVector)).ToList());
                return Task.FromResult(results);
            });
        return new RagService(store, embeddingMock.Object, [], NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task CompareSourcesAsync_SpecNotIngested_ReturnsIngestPrompt()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var llmMock = new Mock<ILlmAnalysisService>();
        var tool = new CompareSourcesTool(
            BuildRagService(storeMock.Object, new float[3]),
            storeMock.Object,
            llmMock.Object,
            NullLogger<CompareSourcesTool>.Instance);

        var output = await tool.CompareSourcesAsync("specs", "backlog");

        Assert.Contains("Ingest it first", output);
        llmMock.Verify(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompareSourcesAsync_AllCovered_ReturnsNoGapsMessage()
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 0, "Feature A description", vec)]);
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Feature A WI", 0.9f)]);

        var llmMock = new Mock<ILlmAnalysisService>();
        llmMock.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Feature A must be implemented" });
        llmMock.Setup(l => l.CheckCoverageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoverageVerdict(CoverageStatus.Covered, "Covered by Feature A WI"));

        var tool = new CompareSourcesTool(
            BuildRagService(storeMock.Object, new float[3]),
            storeMock.Object,
            llmMock.Object,
            NullLogger<CompareSourcesTool>.Instance);

        var output = await tool.CompareSourcesAsync("specs", "backlog");

        Assert.Contains("No gaps found", output);
    }

    [Fact]
    public async Task CompareSourcesAsync_PartiallyCovers_IncludedInPartialSection()
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 0, "Access control requirements", vec)]);
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Basic role assignment", 0.6f)]);

        var llmMock = new Mock<ILlmAnalysisService>();
        llmMock.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Role-based access must restrict sensitive fields" });
        llmMock.Setup(l => l.CheckCoverageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoverageVerdict(CoverageStatus.PartiallyCovers, "WI covers basic roles but not field-level restrictions"));

        var tool = new CompareSourcesTool(
            BuildRagService(storeMock.Object, new float[3]),
            storeMock.Object,
            llmMock.Object,
            NullLogger<CompareSourcesTool>.Instance);

        var output = await tool.CompareSourcesAsync("specs", "backlog");

        Assert.Contains("Partially covered", output);
        Assert.Contains("field-level restrictions", output);
        Assert.Contains("Partial: 1", output);
    }

    [Fact]
    public async Task CompareSourcesAsync_UncoveredRequirement_ReturnsGapReport()
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 0, "Invoice validation rules", vec)]);
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Unrelated WI", 0.3f)]);

        var llmMock = new Mock<ILlmAnalysisService>();
        llmMock.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Invoice amounts must be validated against positive values" });
        llmMock.Setup(l => l.CheckCoverageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoverageVerdict(CoverageStatus.NotCovered, "No matching work item found"));

        var tool = new CompareSourcesTool(
            BuildRagService(storeMock.Object, new float[3]),
            storeMock.Object,
            llmMock.Object,
            NullLogger<CompareSourcesTool>.Instance);

        var output = await tool.CompareSourcesAsync("specs", "backlog");

        Assert.Contains("Not covered", output);
        Assert.Contains("Invoice amounts", output);
        Assert.Contains("No matching work item", output);
    }

    [Fact]
    public async Task CompareSourcesAsync_TooManyRequirements_CapsLlmCallsAndWarns()
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ChunkInfo("specs", "/a.md", 0, "Spec A", vec),
                new ChunkInfo("specs", "/b.md", 0, "Spec B", vec),
                new ChunkInfo("specs", "/c.md", 0, "Spec C", vec),
            ]);
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Some WI", 0.5f)]);

        var llmMock = new Mock<ILlmAnalysisService>();
        llmMock.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 80).Select(i => $"Requirement {i}").ToList());
        llmMock.Setup(l => l.CheckCoverageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CoverageVerdict(CoverageStatus.NotCovered, "Missing"));

        var tool = new CompareSourcesTool(
            BuildRagService(storeMock.Object, new float[3]),
            storeMock.Object,
            llmMock.Object,
            NullLogger<CompareSourcesTool>.Instance);

        var output = await tool.CompareSourcesAsync("specs", "backlog");

        Assert.Contains($"Total requirements analysed: {CompareSourcesTool.MaxRequirements}", output);
        Assert.Contains("WARNING", output);
        // Third file is never sent for extraction once the cap is hit.
        llmMock.Verify(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        llmMock.Verify(l => l.CheckCoverageAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(CompareSourcesTool.MaxRequirements));
    }
}
