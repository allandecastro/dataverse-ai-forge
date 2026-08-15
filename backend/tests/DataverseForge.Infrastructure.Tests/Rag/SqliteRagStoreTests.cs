using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using DataverseForge.Infrastructure.Rag;

namespace DataverseForge.Infrastructure.Tests.Rag;

/// <summary>
/// Unit tests for <see cref="SqliteRagStore"/> using an in-memory SQLite database.
/// No file I/O — ":memory:" is a special SQLite connection string handled entirely within the process.
/// </summary>
public sealed class SqliteRagStoreTests : IDisposable
{
    private readonly SqliteRagStore _store = new(":memory:");

    public void Dispose() => _store.Dispose();

    // Helper: build a unit vector from raw floats.
    private static ReadOnlyMemory<float> Vec(params float[] values) => values.AsMemory();

    // ─── Sources ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertSource_ThenGetSources_ReturnsThatSource()
    {
        await _store.UpsertSourceAsync("s1", "Source One", "filesystem", "/path", SourceCategory.Internal);

        var sources = await _store.GetSourcesAsync();

        Assert.Single(sources);
        Assert.Equal("s1", sources[0].Id);
        Assert.Equal("Source One", sources[0].Name);
        Assert.Equal("filesystem", sources[0].SourceType);
        Assert.Equal(SourceCategory.Internal, sources[0].Category);
        Assert.Null(sources[0].LastIngestedAt);
    }

    [Fact]
    public async Task UpsertSource_Twice_UpdatesExistingRow()
    {
        await _store.UpsertSourceAsync("s1", "Old Name", "filesystem", "/old", SourceCategory.Internal);
        await _store.UpsertSourceAsync("s1", "New Name", "git", "/new", SourceCategory.Backlog);

        var sources = await _store.GetSourcesAsync();

        Assert.Single(sources);
        Assert.Equal("New Name", sources[0].Name);
        Assert.Equal("git", sources[0].SourceType);
        Assert.Equal(SourceCategory.Backlog, sources[0].Category);
    }

    [Fact]
    public async Task MarkIngested_UpdatesLastIngestedAt()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await _store.MarkIngestedAsync("s1");
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var sources = await _store.GetSourcesAsync();
        Assert.NotNull(sources[0].LastIngestedAt);
        Assert.InRange(sources[0].LastIngestedAt!.Value, before, after);
    }

    [Fact]
    public async Task DeleteSource_RemovesSourceAndAllItsChunks()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        await _store.UpsertChunksAsync("s1", "/file.txt", DateTimeOffset.UtcNow,
            [("content", Vec(1f, 0f))]);

        await _store.DeleteSourceAsync("s1");

        Assert.Empty(await _store.GetSourcesAsync());
        Assert.Empty(await _store.GetAllChunksAsync("s1"));
    }

    [Fact]
    public async Task GetSources_MultipleSourcesOrderedById()
    {
        await _store.UpsertSourceAsync("b", "B", "filesystem", null, SourceCategory.Internal);
        await _store.UpsertSourceAsync("a", "A", "filesystem", null, SourceCategory.Internal);

        var sources = await _store.GetSourcesAsync();

        Assert.Equal(2, sources.Count);
        Assert.Equal("a", sources[0].Id); // ORDER BY id ASC
        Assert.Equal("b", sources[1].Id);
    }

    // ─── Chunks ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertChunks_ThenGetAllChunks_RoundTrips()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        var ts = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await _store.UpsertChunksAsync("s1", "/doc.txt", ts,
        [
            ("chunk one", Vec(1f, 0f, 0f)),
            ("chunk two", Vec(0f, 1f, 0f))
        ]);

        var chunks = await _store.GetAllChunksAsync("s1");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("chunk one", chunks[0].Content);
        Assert.Equal("chunk two", chunks[1].Content);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal("s1", chunks[0].SourceId);
    }

    [Fact]
    public async Task UpsertChunks_SameFile_OverwritesPreviousChunks()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        var ts = DateTimeOffset.UtcNow;

        await _store.UpsertChunksAsync("s1", "/doc.txt", ts, [("old content", Vec(1f, 0f))]);
        await _store.UpsertChunksAsync("s1", "/doc.txt", ts, [("new content", Vec(0f, 1f))]);

        var chunks = await _store.GetAllChunksAsync("s1");
        Assert.Single(chunks);
        Assert.Equal("new content", chunks[0].Content);
    }

    [Fact]
    public async Task GetFileLastModified_ReturnsStoredTimestamp()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        var ts = new DateTimeOffset(2024, 6, 15, 12, 30, 0, TimeSpan.Zero);

        await _store.UpsertChunksAsync("s1", "/file.txt", ts, [("content", Vec(1f, 0f))]);

        var result = await _store.GetFileLastModifiedAsync("s1", "/file.txt");

        Assert.NotNull(result);
        Assert.Equal(ts, result!.Value);
    }

    [Fact]
    public async Task GetFileLastModified_UnknownFile_ReturnsNull()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);

        var result = await _store.GetFileLastModifiedAsync("s1", "/nonexistent.txt");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetIndexedFilePaths_ReturnsDistinctFilePaths()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        var ts = DateTimeOffset.UtcNow;
        await _store.UpsertChunksAsync("s1", "/a.txt", ts, [("a1", Vec(1f, 0f)), ("a2", Vec(0f, 1f))]);
        await _store.UpsertChunksAsync("s1", "/b.txt", ts, [("b1", Vec(1f, 1f))]);

        var paths = await _store.GetIndexedFilePathsAsync("s1");

        Assert.Equal(2, paths.Count);
        Assert.Contains("/a.txt", paths);
        Assert.Contains("/b.txt", paths);
    }

    // ─── Search ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchAsync_EmptyStore_ReturnsEmptyList()
    {
        var results = await _store.SearchAsync(Vec(1f, 0f, 0f), topK: 5);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsHighestCosineSimilarityFirst()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        var ts = DateTimeOffset.UtcNow;
        await _store.UpsertChunksAsync("s1", "/file.txt", ts,
        [
            ("orthogonal to query", Vec(0f, 1f, 0f)),  // cosine = 0
            ("identical to query",  Vec(1f, 0f, 0f))   // cosine = 1
        ]);

        var results = await _store.SearchAsync(Vec(1f, 0f, 0f), topK: 5);

        Assert.Equal(2, results.Count);
        Assert.Equal("identical to query", results[0].Content);
        Assert.Equal(1f, results[0].Score, precision: 5);
        Assert.Equal(0f, results[1].Score, precision: 5);
    }

    [Fact]
    public async Task SearchAsync_TopK_LimitsResultCount()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        var ts = DateTimeOffset.UtcNow;
        await _store.UpsertChunksAsync("s1", "/file.txt", ts,
        [
            ("chunk a", Vec(1f, 0f)),
            ("chunk b", Vec(0.9f, 0.1f)),
            ("chunk c", Vec(0.8f, 0.2f))
        ]);

        var results = await _store.SearchAsync(Vec(1f, 0f), topK: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_FilterBySourceIds_OnlyReturnsMatchingSource()
    {
        await _store.UpsertSourceAsync("s1", "S1", "filesystem", null, SourceCategory.Internal);
        await _store.UpsertSourceAsync("s2", "S2", "filesystem", null, SourceCategory.Internal);
        var ts = DateTimeOffset.UtcNow;
        await _store.UpsertChunksAsync("s1", "/a.txt", ts, [("from s1", Vec(1f, 0f))]);
        await _store.UpsertChunksAsync("s2", "/b.txt", ts, [("from s2", Vec(1f, 0f))]);

        var results = await _store.SearchAsync(Vec(1f, 0f), topK: 10, sourceIds: ["s1"]);

        Assert.Single(results);
        Assert.Equal("s1", results[0].SourceId);
        Assert.Equal("from s1", results[0].Content);
    }

    [Fact]
    public async Task SearchAsync_ResultIncludesSourceNameFromSourcesTable()
    {
        await _store.UpsertSourceAsync("s1", "My Source Name", "filesystem", null, SourceCategory.Internal);
        var ts = DateTimeOffset.UtcNow;
        await _store.UpsertChunksAsync("s1", "/file.txt", ts, [("content", Vec(1f, 0f))]);

        var results = await _store.SearchAsync(Vec(1f, 0f), topK: 1);

        Assert.Single(results);
        Assert.Equal("My Source Name", results[0].SourceName);
    }
}
