namespace DataverseForge.Application.Rag;

/// <summary>
/// Splits a plain-text document into overlapping chunks suitable for embedding.
///
/// <para>Strategy: sliding window over whitespace-separated tokens (words).
/// Each chunk contains at most <see cref="ChunkSize"/> tokens; consecutive chunks
/// overlap by <see cref="OverlapSize"/> tokens so that context spanning a chunk
/// boundary is never lost.</para>
///
/// <para>This is a word-based approximation of token count (1 word ≈ 1.3 tokens
/// for English/French). For a target of 400 LLM tokens, a window of ~300 words
/// is safe across all embedding models (ada-002 and text-embedding-3-small have
/// an 8192-token limit; MiniLM-L6 tops out at 256 word-piece tokens so we cap
/// chunks at a much smaller window when using that model).</para>
/// </summary>
public static class TextChunker
{
    /// <summary>Default chunk size in words (≈400 LLM tokens).</summary>
    public const int ChunkSize = 300;

    /// <summary>Default overlap in words (20% of chunk size).</summary>
    public const int OverlapSize = 60;

    /// <summary>
    /// Split <paramref name="text"/> into overlapping chunks.
    /// Empty or whitespace-only input yields an empty sequence.
    /// </summary>
    public static IReadOnlyList<string> Chunk(string text, int chunkSize = ChunkSize, int overlapSize = OverlapSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(chunkSize);
        ArgumentOutOfRangeException.ThrowIfNegative(overlapSize);
        if (overlapSize >= chunkSize)
            throw new ArgumentException("overlapSize must be less than chunkSize.", nameof(overlapSize));

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            return Array.Empty<string>();

        var chunks = new List<string>();
        var step = chunkSize - overlapSize;

        for (var start = 0; start < words.Length; start += step)
        {
            var end = Math.Min(start + chunkSize, words.Length);
            chunks.Add(string.Join(' ', words, start, end - start));

            if (end >= words.Length)
                break;
        }

        return chunks;
    }
}
