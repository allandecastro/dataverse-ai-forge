namespace DataverseForge.Application.Rag;

/// <summary>
/// A pair of passages from two sources that share the same topic area
/// but differ enough in content to be flagged as a potential conflict.
///
/// <para>Detection uses cosine similarity in a "medium score band":
/// <list type="bullet">
///   <item>Score &gt; <c>maxScore</c> — very similar content (same description, not a conflict)</item>
///   <item><c>minScore</c> &lt; Score &lt; <c>maxScore</c> — same topic, different content → <b>potential conflict</b></item>
///   <item>Score &lt; <c>minScore</c> — unrelated topic (not a conflict)</item>
/// </list>
/// A human or the LLM should confirm whether the pair is truly contradictory.</para>
/// </summary>
public sealed record ConflictCandidate(
    string FilePathA,
    int ChunkIndexA,
    string ContentA,
    string FilePathB,
    int ChunkIndexB,
    string ContentB,
    /// <summary>Cosine similarity in the medium band (higher = more likely to be related).</summary>
    float Score);
