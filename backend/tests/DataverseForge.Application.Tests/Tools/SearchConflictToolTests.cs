using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class SearchConflictToolTests
{
    private static RagService BuildRagService(IRagStore store)
    {
        var embeddingMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embeddingMock.Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>((inputs, _, _) =>
            {
                var results = new GeneratedEmbeddings<Embedding<float>>(
                    inputs.Select(_ => new Embedding<float>(new float[3])).ToList());
                return Task.FromResult(results);
            });
        return new RagService(store, embeddingMock.Object, [], NullLogger<RagService>.Instance);
    }

    [Fact]
    public async Task SearchConflictAsync_NoChunksInSourceA_ReturnsNoPairsMessage()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var llmMock = new Mock<ILlmAnalysisService>();
        var tool = new SearchConflictTool(
            BuildRagService(storeMock.Object),
            llmMock.Object,
            NullLogger<SearchConflictTool>.Instance);

        var result = await tool.SearchConflictAsync("specs", "backlog");

        Assert.Contains("No candidate conflict pairs found", result);
        llmMock.Verify(l => l.CheckConflictAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchConflictAsync_LlmDiscardsAllCandidates_ReturnsZeroContradictions()
    {
        var embedding = new ReadOnlyMemory<float>(new float[3]);
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 0, "Amount must be positive", embedding)]);
        // Score 0.6 — within the conflict band [0.45, 0.72]
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 1, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Amount should be non-negative", 0.6f)]);

        var llmMock = new Mock<ILlmAnalysisService>();
        llmMock.Setup(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Complement, "Both say positive / non-negative, semantically equivalent"));

        var tool = new SearchConflictTool(
            BuildRagService(storeMock.Object),
            llmMock.Object,
            NullLogger<SearchConflictTool>.Instance);

        var result = await tool.SearchConflictAsync("specs", "backlog");

        Assert.Contains("0 genuine contradictions", result);
        llmMock.Verify(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchConflictAsync_LlmConfirmsContradiction_ReturnsFormattedReport()
    {
        var embedding = new ReadOnlyMemory<float>(new float[3]);
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 1, "Invoice amount must be strictly positive", embedding)]);
        // Score 0.58 — within band
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 1, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("exports", "Exports", "/export.xml", 3, "Invoice amount can be zero (free invoice)", 0.58f)]);

        var llmMock = new Mock<ILlmAnalysisService>();
        llmMock.Setup(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Contradiction, "Spec disallows zero; export allows zero invoice"));

        var tool = new SearchConflictTool(
            BuildRagService(storeMock.Object),
            llmMock.Object,
            NullLogger<SearchConflictTool>.Instance);

        var result = await tool.SearchConflictAsync("specs", "exports");

        Assert.Contains("1 confirmed contradiction", result);
        Assert.Contains("Contradiction #1", result);
        Assert.Contains("Spec disallows zero", result);
        Assert.Contains("/spec.md", result);
        Assert.Contains("/export.xml", result);
    }

    [Fact]
    public async Task SearchConflictAsync_ChunkScoreAboveBand_IsNotFlagged()
    {
        // Score 0.95 — above maxScore 0.72 → same content, not a conflict
        var embedding = new ReadOnlyMemory<float>(new float[3]);
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 0, "Amount must be positive", embedding)]);
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 1, It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Amount must be positive", 0.95f)]);

        var llmMock = new Mock<ILlmAnalysisService>();
        var tool = new SearchConflictTool(
            BuildRagService(storeMock.Object),
            llmMock.Object,
            NullLogger<SearchConflictTool>.Instance);

        var result = await tool.SearchConflictAsync("specs", "backlog");

        Assert.Contains("No candidate conflict pairs found", result);
        llmMock.Verify(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}