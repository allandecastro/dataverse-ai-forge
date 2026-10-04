using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Application.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class SearchConflictToolTests
{
    private static readonly ReadOnlyMemory<float> Vec = new(new float[3]);

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

    private static Mock<IRagStore> StoreWith(IReadOnlyList<ChunkInfo> sourceAChunks, IReadOnlyList<RagResult> bMatches)
    {
        var store = new Mock<IRagStore>();
        store.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>())).ReturnsAsync(sourceAChunks);
        store.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bMatches);
        return store;
    }

    private static SearchConflictTool BuildTool(Mock<IRagStore> store, Mock<ILlmAnalysisService> llm) =>
        new(BuildRagService(store.Object), store.Object, llm.Object, NullLogger<SearchConflictTool>.Instance);

    [Fact]
    public async Task SearchConflictAsync_NoChunksInSourceA_ReturnsIngestPrompt()
    {
        var store = StoreWith([], []);
        var llm = new Mock<ILlmAnalysisService>();

        var result = await BuildTool(store, llm).SearchConflictAsync("specs", "backlog");

        Assert.Contains("Ingest it first", result);
        llm.Verify(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchConflictAsync_NearIdenticalSentencesWithDifferentThreshold_IsReported()
    {
        // The case the old cosine band [0.45, 0.72] missed: same sentence, different value,
        // so the similarity is very high. Every statement must still reach the LLM.
        var store = StoreWith(
            [new ChunkInfo("specs", "/spec.md", 0, "Expense reports above 500 EUR must be approved by the manager.", Vec)],
            [new RagResult("backlog", "Backlog", "/wi.md", 0, "WI-102: Manager approval for reports above 1000 EUR.", 0.97f)]);
        var llm = new Mock<ILlmAnalysisService>();
        llm.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Expense reports above 500 EUR must be approved by the manager."]);
        llm.Setup(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Contradiction, "Spec says 500 EUR, backlog says 1000 EUR."));

        var result = await BuildTool(store, llm).SearchConflictAsync("specs", "backlog");

        Assert.Contains("Found 1 confirmed contradiction", result);
        Assert.Contains("above 500 EUR", result);
        Assert.Contains("WI-102", result);
        Assert.Contains("Spec says 500 EUR, backlog says 1000 EUR.", result);
    }

    [Fact]
    public async Task SearchConflictAsync_ComplementAndUnrelated_AreNotReported()
    {
        var store = StoreWith(
            [new ChunkInfo("specs", "/spec.md", 0, "Spec text", Vec)],
            [new RagResult("backlog", "Backlog", "/wi.md", 0, "Some work item", 0.8f)]);
        var llm = new Mock<ILlmAnalysisService>();
        llm.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Statement one is long enough", "Statement two is long enough"]);
        llm.SetupSequence(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Complement, "Consistent"))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Unrelated, "Silent"));

        var result = await BuildTool(store, llm).SearchConflictAsync("specs", "backlog");

        Assert.Contains("Checked 2 statement(s)", result);
        Assert.Contains("no genuine contradictions", result);
    }

    [Fact]
    public async Task SearchConflictAsync_StatementWithNoMatchInB_IsSkippedNotSentToLlm()
    {
        var store = StoreWith([new ChunkInfo("specs", "/spec.md", 0, "Spec text", Vec)], []);
        var llm = new Mock<ILlmAnalysisService>();
        llm.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["A statement with no counterpart"]);

        var result = await BuildTool(store, llm).SearchConflictAsync("specs", "backlog");

        Assert.Contains("1 statement(s) had no matching passage", result);
        llm.Verify(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchConflictAsync_MultipleContradictions_AllIncludedInReport()
    {
        var store = StoreWith(
            [new ChunkInfo("specs", "/spec.md", 0, "Spec text", Vec)],
            [new RagResult("backlog", "Backlog", "/wi.md", 0, "Backlog text", 0.9f)]);
        var llm = new Mock<ILlmAnalysisService>();
        llm.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(["Reimbursement within 15 days", "Rejection reason is mandatory"]);
        llm.SetupSequence(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Contradiction, "15 vs 30 days"))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Contradiction, "mandatory vs optional"));

        var result = await BuildTool(store, llm).SearchConflictAsync("specs", "backlog");

        Assert.Contains("Found 2 confirmed contradiction(s)", result);
        Assert.Contains("Contradiction #1", result);
        Assert.Contains("Contradiction #2", result);
        Assert.Contains("15 vs 30 days", result);
        Assert.Contains("mandatory vs optional", result);
    }

    [Fact]
    public async Task SearchConflictAsync_TooManyStatements_CapsLlmCallsAndWarns()
    {
        var store = StoreWith(
            [
                new ChunkInfo("specs", "/a.md", 0, "A", Vec),
                new ChunkInfo("specs", "/b.md", 0, "B", Vec),
                new ChunkInfo("specs", "/c.md", 0, "C", Vec),
            ],
            [new RagResult("backlog", "Backlog", "/wi.md", 0, "Backlog text", 0.9f)]);
        var llm = new Mock<ILlmAnalysisService>();
        llm.Setup(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 80).Select(i => $"Statement number {i}").ToList());
        llm.Setup(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConflictVerdict(ConflictRelation.Unrelated, "n/a"));

        var result = await BuildTool(store, llm).SearchConflictAsync("specs", "backlog");

        Assert.Contains("WARNING", result);
        llm.Verify(l => l.ExtractRequirementsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        llm.Verify(l => l.CheckConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(SearchConflictTool.MaxStatements));
    }
}
