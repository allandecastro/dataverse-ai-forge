using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Rag;

/// <summary>
/// Orchestrates the RAG pipeline: incremental ingestion and cascading similarity search.
///
/// <para><b>Ingestion</b> is incremental: a file is re-chunked and re-embedded only if its
/// <c>LastModified</c> timestamp has changed since the last ingest. Files removed from a source
/// are automatically pruned from the store.</para>
///
/// <para><b>Search</b> is cascading by <see cref="IDocumentSource.Priority"/>:
/// sources are grouped by ascending priority value and searched one group at a time.
/// If the best score in the current group reaches <see cref="SearchScoreThreshold"/>,
/// lower-priority groups are skipped entirely. This lets the agent honour instructions like
/// "prefer validated specs over meeting transcripts".</para>
/// </summary>
public sealed class RagService(
    IRagStore store,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IEnumerable<IDocumentSource> documentSources,
    ILogger<RagService> logger)
{
    /// <summary>
    /// Cosine-similarity score at or above which a priority group is considered sufficient
    /// and lower-priority groups are not queried. Configurable via constructor overload;
    /// default matches the plan specification.
    /// </summary>
    public float SearchScoreThreshold { get; init; } = 0.75f;

    private readonly IReadOnlyList<IDocumentSource> _sources = documentSources.ToList();

    // ─── Ingest ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ingest all documents from the source identified by <paramref name="sourceId"/>.
    /// Pass <c>null</c> to ingest all registered sources.
    /// </summary>
    public async Task<IngestSummary> IngestAsync(string? sourceId, CancellationToken cancellationToken = default)
    {
        var targets = sourceId is null
            ? _sources
            : _sources.Where(s => s.Id == sourceId).ToList();

        if (targets.Count == 0)
            return new IngestSummary(0, 0, 0, $"No source found with id '{sourceId}'.");

        // Hoist the store metadata read once for all targets (avoids N+1 reads in the loop).
        var knownSources = (await store.GetSourcesAsync(cancellationToken))
            .ToDictionary(s => s.Id, s => s);

        var totalDocs = 0;
        var totalChunks = 0;
        var skippedDocs = 0;

        foreach (var source in targets)
        {
            var summary = await IngestSourceCoreAsync(source, knownSources, cancellationToken);
            if (summary.ErrorMessage is not null)
                logger.LogWarning("Error ingesting '{SourceId}': {Error}", source.Id, summary.ErrorMessage);
            totalDocs += summary.DocumentsIngested;
            totalChunks += summary.ChunksIngested;
            skippedDocs += summary.DocumentsSkipped;
        }

        logger.LogInformation("Ingest complete — {Docs} docs, {Chunks} chunks, {Skipped} skipped", totalDocs, totalChunks, skippedDocs);
        return new IngestSummary(totalDocs, totalChunks, skippedDocs, null);
    }

    /// <summary>
    /// Ingest a dynamically-created source that is not registered in the DI container.
    /// Used by <c>IngestDocumentsTool</c> for filesystem and git sources supplied at runtime.
    /// </summary>
    public async Task<IngestSummary> IngestSourceAsync(IDocumentSource source, CancellationToken cancellationToken = default)
    {
        var knownSources = (await store.GetSourcesAsync(cancellationToken))
            .ToDictionary(s => s.Id, s => s);
        return await IngestSourceCoreAsync(source, knownSources, cancellationToken);
    }

    private async Task<IngestSummary> IngestSourceCoreAsync(
        IDocumentSource source,
        IReadOnlyDictionary<string, SourceInfo> knownSources,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Ingesting source '{SourceId}' ({SourceName})", source.Id, source.Name);

        // Register/update metadata. Does NOT update last_ingested_at — that is done at the END
        // of a successful ingest so a mid-run failure doesn't poison the incremental timestamp.
        await store.UpsertSourceAsync(source.Id, source.Name, GetSourceType(source), GetSourcePath(source), source.Category, cancellationToken);

        // For incremental sources, pass the last successful ingest time so they can filter.
        var lastIngestedAt = knownSources.TryGetValue(source.Id, out var info) ? info.LastIngestedAt : null;
        var changedSince = source.SupportsIncrementalFetch ? lastIngestedAt : null;
        var isIncremental = changedSince.HasValue;

        if (isIncremental)
            logger.LogInformation("Incremental ingest for '{SourceId}' since {ChangedSince:O}", source.Id, changedSince);

        var existingPaths = await store.GetIndexedFilePathsAsync(source.Id, cancellationToken);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var totalDocs = 0;
        var totalChunks = 0;
        var skippedDocs = 0;

        await foreach (var doc in source.GetDocumentsAsync(changedSince, cancellationToken))
        {
            seenPaths.Add(doc.FilePath);

            var storedLastModified = await store.GetFileLastModifiedAsync(source.Id, doc.FilePath, cancellationToken);
            if (storedLastModified.HasValue && storedLastModified.Value >= doc.LastModified)
            {
                skippedDocs++;
                continue;
            }

            var chunks = TextChunker.Chunk(doc.Content);
            if (chunks.Count == 0)
                continue;

            var embeddingResults = await embeddingGenerator.GenerateAsync(
                chunks.ToList(), cancellationToken: cancellationToken);

            var chunksWithEmbeddings = chunks
                .Zip(embeddingResults, (text, emb) => (text, emb.Vector))
                .ToList();

            await store.UpsertChunksAsync(source.Id, doc.FilePath, doc.LastModified, chunksWithEmbeddings, cancellationToken);

            totalDocs++;
            totalChunks += chunks.Count;
            logger.LogDebug("Ingested '{FilePath}': {ChunkCount} chunks", doc.FilePath, chunks.Count);
        }

        // Prune stale files only on a full ingest (incremental returns partial results — absent = unchanged).
        if (!isIncremental)
        {
            foreach (var stale in existingPaths.Except(seenPaths, StringComparer.OrdinalIgnoreCase))
            {
                logger.LogInformation("Pruning stale '{FilePath}' from '{SourceId}'", stale, source.Id);
                await store.UpsertChunksAsync(source.Id, stale, DateTimeOffset.MinValue, [], cancellationToken);
            }
        }

        // Record completion AFTER a successful run so failures don't poison the incremental timestamp.
        await store.MarkIngestedAsync(source.Id, cancellationToken);

        return new IngestSummary(totalDocs, totalChunks, skippedDocs, null);
    }

    // ─── Search ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Cascading similarity search. Sources are queried by category in the order defined
    /// by <paramref name="intent"/> (see <see cref="SearchIntent"/>).
    /// Once a category group yields a result above <see cref="SearchScoreThreshold"/> the
    /// remaining groups are not queried.
    /// If <paramref name="intent"/> is null or unknown, all sources are searched simultaneously.
    /// </summary>
    public async Task<IReadOnlyList<RagResult>> SearchAsync(string query, int topK = 5, string? intent = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<RagResult>();

        var queryEmbedding = (await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken))
            .First().Vector;

        var registeredSources = await store.GetSourcesAsync(cancellationToken);

        var categoryOrder = SearchIntent.GetCategoryOrder(intent);

        if (categoryOrder is null)
        {
            // General intent: search all sources at once
            logger.LogDebug("Search intent=null — searching all sources simultaneously");
            return await store.SearchAsync(queryEmbedding, topK, null, cancellationToken);
        }

        // Cascading search: iterate categories in intent order, stop when threshold is met.
        var sourcesByCategory = registeredSources
            .GroupBy(s => s.Category)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());

        foreach (var category in categoryOrder)
        {
            if (!sourcesByCategory.TryGetValue(category, out var sourceIds) || sourceIds.Count == 0)
                continue;

            var results = await store.SearchAsync(queryEmbedding, topK, sourceIds, cancellationToken);

            if (results.Count > 0 && results[0].Score >= SearchScoreThreshold)
            {
                logger.LogDebug("Search satisfied by category {Category} (intent={Intent}, best score {Score:F3})",
                    category, intent, results[0].Score);
                return results;
            }
        }

        // Fallback: search across all sources
        logger.LogDebug("No category group met threshold {Threshold} (intent={Intent}); falling back to full search",
            SearchScoreThreshold, intent);
        return await store.SearchAsync(queryEmbedding, topK, null, cancellationToken);
    }

    /// <summary>
    /// Embed <paramref name="query"/> and search only the specified sources.
    /// Unlike <see cref="SearchAsync(string,int,string?,CancellationToken)"/> which
    /// resolves sources via intent/category, this method targets specific source IDs
    /// directly — used by gap-analysis to search the backlog by ID.
    /// </summary>
    public async Task<IReadOnlyList<RagResult>> SearchBySourcesAsync(
        string query,
        int topK,
        IReadOnlyList<string> sourceIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || sourceIds.Count == 0)
            return [];

        var queryEmbedding = (await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken))
            .First().Vector;
        return await store.SearchAsync(queryEmbedding, topK, sourceIds, cancellationToken);
    }

    // ─── Conflict Detection ──────────────────────────────────────────────────

    /// <summary>
    /// Find passages that appear in both sources but with content that diverges enough
    /// to be potentially contradictory. Uses a "medium similarity band":
    /// <list type="bullet">
    ///   <item>Score above <paramref name="maxScore"/> → same content (not a conflict).</item>
    ///   <item>Score in [<paramref name="minScore"/>, <paramref name="maxScore"/>] → same topic, different wording → <b>flagged</b>.</item>
    ///   <item>Score below <paramref name="minScore"/> → unrelated topic (not a conflict).</item>
    /// </list>
    /// The LLM in the Documentary Agent reviews the returned pairs and decides
    /// whether they are truly contradictory.
    /// </summary>
    public async Task<IReadOnlyList<ConflictCandidate>> SearchConflictAsync(
        string sourceAId,
        string sourceBId,
        string? query = null,
        float minScore = 0.45f,
        float maxScore = 0.72f,
        int topK = 10,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Conflict search: A='{SourceA}' vs B='{SourceB}' band=[{Min:F2},{Max:F2}] query='{Query}'",
            sourceAId, sourceBId, minScore, maxScore, query ?? "(all)");

        IReadOnlyList<ChunkInfo> sourceAChunks;

        if (!string.IsNullOrWhiteSpace(query))
        {
            // Narrow sourceA to the most query-relevant chunks first
            var queryEmbedding = (await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken))
                .First().Vector;
            var topResults = await store.SearchAsync(queryEmbedding, topK * 2, [sourceAId], cancellationToken);

            // Re-create ChunkInfo from search results so we have embeddings
            var allAChunks = await store.GetAllChunksAsync(sourceAId, cancellationToken);
            var relevantPaths = topResults.Select(r => (r.FilePath, r.ChunkIndex)).ToHashSet();
            sourceAChunks = allAChunks
                .Where(c => relevantPaths.Contains((c.FilePath, c.ChunkIndex)))
                .ToList();
        }
        else
        {
            sourceAChunks = await store.GetAllChunksAsync(sourceAId, cancellationToken);
        }

        if (sourceAChunks.Count == 0)
        {
            logger.LogWarning("No chunks found for source '{SourceA}' — ingest it before conflict search.", sourceAId);
            return [];
        }

        var bIds = new[] { sourceBId };
        var candidates = new List<(ConflictCandidate Candidate, float Score)>();

        foreach (var chunkA in sourceAChunks)
        {
            var matches = await store.SearchAsync(chunkA.Embedding, 1, bIds, cancellationToken);
            if (matches.Count == 0) continue;

            var best = matches[0];
            if (best.Score >= minScore && best.Score < maxScore)
            {
                candidates.Add((new ConflictCandidate(
                    chunkA.FilePath, chunkA.ChunkIndex, chunkA.Content,
                    best.FilePath, best.ChunkIndex, best.Content,
                    best.Score), best.Score));
            }
        }

        logger.LogInformation("Conflict search: {Total} chunks scanned, {Flagged} candidates in band [{Min:F2},{Max:F2}]",
            sourceAChunks.Count, candidates.Count, minScore, maxScore);

        return candidates
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Candidate)
            .ToList();
    }

    // ─── Gap Analysis ────────────────────────────────────────────────────────

    /// <summary>
    /// Compare two ingested sources and return passages from <paramref name="specSourceId"/>
    /// that have no semantically similar match in <paramref name="backlogSourceId"/>.
    ///
    /// <para>Algorithm: for each chunk stored in the spec source, search the backlog source
    /// for the top-1 nearest neighbour. If the best score is below <paramref name="threshold"/>
    /// the passage is considered "not covered".</para>
    ///
    /// <para>Default threshold 0.65 is deliberately lower than <see cref="SearchScoreThreshold"/>
    /// (0.75) to lean toward false-positives (reporting more gaps) rather than silently
    /// missing them.</para>
    /// </summary>
    public async Task<GapAnalysisResult> CompareSourcesAsync(
        string specSourceId,
        string backlogSourceId,
        float threshold = 0.65f,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Gap analysis: spec='{SpecId}' vs backlog='{BacklogId}' threshold={Threshold:F2}",
            specSourceId, backlogSourceId, threshold);

        var specChunks = await store.GetAllChunksAsync(specSourceId, cancellationToken);

        if (specChunks.Count == 0)
        {
            logger.LogWarning("No chunks found for source '{SpecId}' — ingest it before running gap analysis.", specSourceId);
            return new GapAnalysisResult(specSourceId, backlogSourceId, 0, [], threshold);
        }

        var backlogIds = new[] { backlogSourceId };
        var uncovered = new List<UncoveredPassage>();

        foreach (var chunk in specChunks)
        {
            var matches = await store.SearchAsync(chunk.Embedding, 1, backlogIds, cancellationToken);
            var bestScore = matches.Count > 0 ? matches[0].Score : 0f;

            if (bestScore < threshold)
                uncovered.Add(new UncoveredPassage(chunk.FilePath, chunk.ChunkIndex, chunk.Content, bestScore));
        }

        logger.LogInformation("Gap analysis complete: {Total} chunks, {Uncovered} uncovered (threshold={Threshold:F2})",
            specChunks.Count, uncovered.Count, threshold);

        return new GapAnalysisResult(specSourceId, backlogSourceId, specChunks.Count, uncovered, threshold);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static string GetSourceType(IDocumentSource source) => source.GetType().Name switch
    {
        var n when n.Contains("Filesystem", StringComparison.OrdinalIgnoreCase) => "filesystem",
        var n when n.Contains("ForgeExports", StringComparison.OrdinalIgnoreCase) => "exports",
        var n when n.Contains("AzureDevOps", StringComparison.OrdinalIgnoreCase) => "azdo",
        var n when n.Contains("GitRepository", StringComparison.OrdinalIgnoreCase) => "git",
        _ => "unknown"
    };

    private static string? GetSourcePath(IDocumentSource source)
    {
        // Expose the path property if the concrete type has one (filesystem sources).
        var pathProp = source.GetType().GetProperty("Path");
        return pathProp?.GetValue(source) as string;
    }
}

/// <summary>Summary of an incremental ingest operation returned to the calling tool.</summary>
public sealed record IngestSummary(int DocumentsIngested, int ChunksIngested, int DocumentsSkipped, string? ErrorMessage);
