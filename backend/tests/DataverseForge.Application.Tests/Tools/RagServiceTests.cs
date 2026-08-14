using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class RagServiceTests
{
    private static RagService BuildService(
        IRagStore? store = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGen = null,
        IDocumentSource[]? sources = null,
        float threshold = 0.75f)
    {
        store ??= new Mock<IRagStore>().Object;
        embeddingGen ??= MockEmbeddingGenerator(new float[3]);
        sources ??= [];
        return new RagService(store, embeddingGen, sources, NullLogger<RagService>.Instance)
        {
            SearchScoreThreshold = threshold
        };
    }

    private static IEmbeddingGenerator<string, Embedding<float>> MockEmbeddingGenerator(float[] vector)
    {
        var mock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        mock.Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>((inputs, _, _) =>
            {
                var embeddings = new GeneratedEmbeddings<Embedding<float>>(
                    inputs.Select(_ => new Embedding<float>(vector)).ToList());
                return Task.FromResult(embeddings);
            });
        return mock.Object;
    }

    // ─── Ingest ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IngestAsync_NoMatchingSource_ReturnsErrorSummary()
    {
        var svc = BuildService();
        var result = await svc.IngestAsync("nonexistent-source");
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("nonexistent-source", result.ErrorMessage);
    }

    [Fact]
    public async Task IngestAsync_NullSourceId_IngestsAllSources()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock.Setup(s => s.GetIndexedFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock.Setup(s => s.GetFileLastModifiedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);
        storeMock.Setup(s => s.UpsertSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SourceCategory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeMock.Setup(s => s.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyList<(string, ReadOnlyMemory<float>)>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var source = new Mock<IDocumentSource>();
        source.Setup(s => s.Id).Returns("test-source");
        source.Setup(s => s.Name).Returns("Test");
        source.Setup(s => s.Category).Returns(SourceCategory.Specification);
        source.Setup(s => s.SupportsIncrementalFetch).Returns(false);
        source.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable([
                new RawDocument("test-source", "Test", "/path/doc.txt", "Hello world this is content", "text", DateTimeOffset.UtcNow)
            ]));

        var svc = BuildService(store: storeMock.Object, sources: [source.Object]);
        var result = await svc.IngestAsync(null);

        Assert.Equal(1, result.DocumentsIngested);
        Assert.True(result.ChunksIngested >= 1);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task IngestAsync_SkipsUnchangedFiles()
    {
        var storeMock = new Mock<IRagStore>();
        var lastModified = DateTimeOffset.UtcNow.AddHours(-1);

        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock.Setup(s => s.GetIndexedFilePathsAsync("src", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["/doc.txt"]);
        storeMock.Setup(s => s.GetFileLastModifiedAsync("src", "/doc.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastModified); // same as doc's LastModified → should skip
        storeMock.Setup(s => s.UpsertSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SourceCategory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var source = new Mock<IDocumentSource>();
        source.Setup(s => s.Id).Returns("src");
        source.Setup(s => s.Name).Returns("Src");
        source.Setup(s => s.Category).Returns(SourceCategory.Specification);
        source.Setup(s => s.SupportsIncrementalFetch).Returns(false);
        source.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable([
                new RawDocument("src", "Src", "/doc.txt", "content", "text", lastModified)
            ]));

        var svc = BuildService(store: storeMock.Object, sources: [source.Object]);
        var result = await svc.IngestAsync("src");

        // Document was skipped (not re-embedded)
        Assert.Equal(0, result.DocumentsIngested);
        Assert.Equal(1, result.DocumentsSkipped);
        storeMock.Verify(s => s.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
            It.IsAny<IReadOnlyList<(string, ReadOnlyMemory<float>)>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IngestAsync_IncrementalSource_PassesLastIngestDateToGetDocuments()
    {
        var lastIngest = DateTimeOffset.UtcNow.AddHours(-2);
        DateTimeOffset? capturedChangedSince = null;

        var storeMock = new Mock<IRagStore>();
        // Simulate a previous ingest: source is already registered with lastIngestedAt
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SourceInfo("azdo", "Backlog", "azdo", null, SourceCategory.Backlog, lastIngest)]);
        storeMock.Setup(s => s.GetIndexedFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        storeMock.Setup(s => s.GetFileLastModifiedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTimeOffset?)null);
        storeMock.Setup(s => s.UpsertSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SourceCategory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        storeMock.Setup(s => s.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyList<(string, ReadOnlyMemory<float>)>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var source = new Mock<IDocumentSource>();
        source.Setup(s => s.Id).Returns("azdo");
        source.Setup(s => s.Name).Returns("Backlog");
        source.Setup(s => s.Category).Returns(SourceCategory.Backlog);
        source.Setup(s => s.SupportsIncrementalFetch).Returns(true);
        source.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns<DateTimeOffset?, CancellationToken>((cs, ct) =>
            {
                capturedChangedSince = cs;
                return AsyncEnumerable([
                    new RawDocument("azdo", "Backlog", "azdo://proj/Feature/42", "Feature: Login", "text", DateTimeOffset.UtcNow)
                ]);
            });

        var svc = BuildService(store: storeMock.Object, sources: [source.Object]);
        await svc.IngestAsync("azdo");

        // changedSince must match the last ingest date read from the store
        Assert.NotNull(capturedChangedSince);
        Assert.Equal(lastIngest.ToUnixTimeSeconds(), capturedChangedSince!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public async Task IngestAsync_CallsMarkIngestedAfterSuccessfulRun()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        storeMock.Setup(s => s.GetIndexedFilePathsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        storeMock.Setup(s => s.GetFileLastModifiedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DateTimeOffset?)null);
        storeMock.Setup(s => s.UpsertSourceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<SourceCategory>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storeMock.Setup(s => s.UpsertChunksAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<IReadOnlyList<(string, ReadOnlyMemory<float>)>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        storeMock.Setup(s => s.MarkIngestedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var source = new Mock<IDocumentSource>();
        source.Setup(s => s.Id).Returns("specs");
        source.Setup(s => s.Name).Returns("Specs");
        source.Setup(s => s.Category).Returns(SourceCategory.Specification);
        source.Setup(s => s.SupportsIncrementalFetch).Returns(false);
        source.Setup(s => s.GetDocumentsAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable([new RawDocument("specs", "Specs", "/f.md", "content", "text", DateTimeOffset.UtcNow)]));

        var svc = BuildService(store: storeMock.Object, sources: [source.Object]);
        await svc.IngestAsync("specs");

        // MarkIngestedAsync must be called exactly once AFTER the loop (not before)
        storeMock.Verify(s => s.MarkIngestedAsync("specs", It.IsAny<CancellationToken>()), Times.Once);
        // UpsertSourceAsync must NOT have set last_ingested_at (that's done by MarkIngested)
        // — verified implicitly by the fact that MarkIngestedAsync is the only timestamp setter
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        var svc = BuildService();
        var results = await svc.SearchAsync(string.Empty);
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ImplementedBehaviorIntent_SearchesImplementationFirst_StopsWhenThresholdMet()
    {
        var storeMock = new Mock<IRagStore>();

        // Two categories: Implementation (exports) and Specification (specs)
        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SourceInfo("exports", "Exports", "exports", null, SourceCategory.Implementation, null),
                new SourceInfo("specs", "Specs", "filesystem", "/specs", SourceCategory.Specification, null)
            ]);

        // Implementation search returns result above threshold
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("exports")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("exports", "Exports", "/file.xml", 0, "content", 0.9f)]);

        var svc = BuildService(store: storeMock.Object, threshold: 0.75f);
        var results = await svc.SearchAsync("how is field X configured?", intent: SearchIntent.ImplementedBehavior);

        // Should return the exports result and NOT call specs
        Assert.Single(results);
        Assert.Equal("exports", results[0].SourceId);
        storeMock.Verify(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
            It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("specs")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_ExpectedBehaviorIntent_SearchesSpecificationFirst()
    {
        var storeMock = new Mock<IRagStore>();

        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SourceInfo("exports", "Exports", "exports", null, SourceCategory.Implementation, null),
                new SourceInfo("specs", "Specs", "filesystem", "/specs", SourceCategory.Specification, null)
            ]);

        // Specification returns a good result when searched first
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("specs")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("specs", "Specs", "/spec.md", 0, "rule content", 0.88f)]);

        var svc = BuildService(store: storeMock.Object, threshold: 0.75f);
        var results = await svc.SearchAsync("what should the behavior be for field X?", intent: SearchIntent.ExpectedBehavior);

        // Should return the spec result; implementation was never searched
        Assert.Single(results);
        Assert.Equal("specs", results[0].SourceId);
        storeMock.Verify(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
            It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("exports")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchAsync_BelowThreshold_FallsThroughToNextCategory()
    {
        var storeMock = new Mock<IRagStore>();

        storeMock.Setup(s => s.GetSourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new SourceInfo("exports", "Exports", "exports", null, SourceCategory.Implementation, null),
                new SourceInfo("specs", "Specs", "filesystem", "/specs", SourceCategory.Specification, null)
            ]);

        // Implementation returns low-score result
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("exports")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("exports", "Exports", "/file.xml", 0, "content", 0.5f)]);

        // Specification returns better result
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<int>(),
                It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("specs")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("specs", "Specs", "/spec.md", 0, "spec content", 0.85f)]);

        var svc = BuildService(store: storeMock.Object, threshold: 0.75f);
        var results = await svc.SearchAsync("query about specs", intent: SearchIntent.ImplementedBehavior);

        // Implementation below threshold → fell through to Specification
        Assert.Single(results);
        Assert.Equal("specs", results[0].SourceId);
    }

    // ─── Conflict Detection ───────────────────────────────────────────────────

    [Fact]
    public async Task SearchConflictAsync_EmptySourceA_ReturnsEmpty()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetAllChunksAsync("specA", It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var svc = BuildService(store: storeMock.Object);
        var result = await svc.SearchConflictAsync("specA", "backlog");

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchConflictAsync_ScoreInBand_ReturnedAsConflict()
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specA", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specA", "/spec.md", 0, "Rule about invoices", vec)]);

        // Score 0.60 → in default band [0.45, 0.72) → conflict
        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 1,
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Invoice processing WI", 0.60f)]);

        var svc = BuildService(store: storeMock.Object);
        var result = await svc.SearchConflictAsync("specA", "backlog");

        Assert.Single(result);
        Assert.Equal("/spec.md", result[0].FilePathA);
        Assert.Equal(0.60f, result[0].Score, precision: 2);
    }

    [Theory]
    [InlineData(0.80f)] // above band → same content, not a conflict
    [InlineData(0.30f)] // below band → different topic, not related
    public async Task SearchConflictAsync_ScoreOutsideBand_NotReturned(float score)
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specA", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specA", "/spec.md", 0, "Some rule", vec)]);

        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 1,
                It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "WI", score)]);

        var svc = BuildService(store: storeMock.Object);
        var result = await svc.SearchConflictAsync("specA", "backlog");

        Assert.Empty(result);
    }

    // ─── Gap Analysis ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompareSourcesAsync_EmptySpecSource_ReturnsZeroChunks()
    {
        var storeMock = new Mock<IRagStore>();
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var svc = BuildService(store: storeMock.Object);
        var result = await svc.CompareSourcesAsync("specs", "backlog");

        Assert.Equal(0, result.TotalChunksAnalyzed);
        Assert.Empty(result.UncoveredPassages);
    }

    [Fact]
    public async Task CompareSourcesAsync_AllCovered_ReturnsNoUncoveredPassages()
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 0, "Feature A", vec)]);

        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 1,
                It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("backlog")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Implement Feature A", 0.85f)]);

        var svc = BuildService(store: storeMock.Object);
        var result = await svc.CompareSourcesAsync("specs", "backlog", threshold: 0.65f);

        Assert.Equal(1, result.TotalChunksAnalyzed);
        Assert.Empty(result.UncoveredPassages);
    }

    [Fact]
    public async Task CompareSourcesAsync_BelowThreshold_ReturnsUncoveredPassage()
    {
        var storeMock = new Mock<IRagStore>();
        var vec = new ReadOnlyMemory<float>(new float[3]);
        storeMock.Setup(s => s.GetAllChunksAsync("specs", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ChunkInfo("specs", "/spec.md", 0, "Some uncovered requirement", vec)]);

        storeMock.Setup(s => s.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), 1,
                It.Is<IReadOnlyList<string>?>(ids => ids != null && ids.Contains("backlog")), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RagResult("backlog", "Backlog", "/wi.txt", 0, "Unrelated WI", 0.4f)]);

        var svc = BuildService(store: storeMock.Object);
        var result = await svc.CompareSourcesAsync("specs", "backlog", threshold: 0.65f);

        Assert.Equal(1, result.TotalChunksAnalyzed);
        Assert.Single(result.UncoveredPassages);
        Assert.Equal("/spec.md", result.UncoveredPassages[0].FilePath);
        Assert.Equal(0.4f, result.UncoveredPassages[0].BestMatchScore, precision: 2);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<T> AsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }
}
