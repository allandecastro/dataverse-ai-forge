using DataverseForge.Application.Rag;

namespace DataverseForge.Application.Contracts;

/// <summary>
/// Uses an LLM to perform reasoning-heavy document analysis tasks that pure
/// vector similarity cannot handle reliably:
/// <list type="bullet">
///   <item><b>Requirement extraction</b> — decompose spec text into discrete, testable statements.</item>
///   <item><b>Coverage check</b> — determine whether a requirement is addressed by candidate work items.</item>
///   <item><b>Conflict check</b> — determine whether two passages make contradictory claims.</item>
/// </list>
/// </summary>
public interface ILlmAnalysisService
{
    /// <summary>
    /// Extract discrete, testable requirements from a block of specification text.
    /// Returns a list of atomic requirement strings (one statement each).
    /// Batches of up to ~3,000 words can be processed in one call; larger texts
    /// should be split by file/section before calling.
    /// </summary>
    Task<IReadOnlyList<string>> ExtractRequirementsAsync(
        string specText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether <paramref name="requirement"/> is covered by any passage in
    /// <paramref name="candidatePassages"/> (typically RAG search results from the
    /// coverage source).
    /// </summary>
    Task<CoverageVerdict> CheckCoverageAsync(
        string requirement,
        IReadOnlyList<string> candidatePassages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determine whether <paramref name="passageA"/> and <paramref name="passageB"/>
    /// are contradictory, complementary, or unrelated.
    /// Used to confirm or discard cosine-similarity conflict candidates.
    /// </summary>
    Task<ConflictVerdict> CheckConflictAsync(
        string passageA,
        string passageB,
        CancellationToken cancellationToken = default);
}
