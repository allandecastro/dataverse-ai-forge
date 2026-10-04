namespace DataverseForge.Application.Rag;

/// <summary>
/// A passage from the reference source that has no semantically similar match
/// in the coverage source (score below the configured threshold).
/// </summary>
public sealed record UncoveredPassage(
    string FilePath,
    int ChunkIndex,
    string Content,
    /// <summary>Best cosine-similarity score found in the coverage source (0.0 = no match).</summary>
    float BestMatchScore);

/// <summary>
/// Result of a cross-source gap analysis produced by <see cref="RagService.CompareSourcesAsync"/>.
/// </summary>
public sealed record GapAnalysisResult(
    string SpecSourceId,
    string BacklogSourceId,
    int TotalChunksAnalyzed,
    IReadOnlyList<UncoveredPassage> UncoveredPassages,
    float Threshold);
