using DataverseForge.Application.Rag;

namespace DataverseForge.Application.Tests.Tools;

public class TextChunkerTests
{
    [Fact]
    public void Chunk_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(TextChunker.Chunk(string.Empty));
        Assert.Empty(TextChunker.Chunk("   "));
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var text = "Hello world this is a short document.";
        var chunks = TextChunker.Chunk(text, chunkSize: 100);
        Assert.Single(chunks);
        Assert.Contains("Hello", chunks[0]);
    }

    [Fact]
    public void Chunk_LongText_ProducesMultipleChunks()
    {
        // Generate 700 words
        var words = Enumerable.Range(0, 700).Select(i => $"word{i}");
        var text = string.Join(" ", words);

        var chunks = TextChunker.Chunk(text, chunkSize: 300, overlapSize: 60);

        // Should produce at least 3 chunks
        Assert.True(chunks.Count >= 3, $"Expected >= 3 chunks, got {chunks.Count}");
    }

    [Fact]
    public void Chunk_ProducesOverlap_BetweenConsecutiveChunks()
    {
        var words = Enumerable.Range(0, 400).Select(i => $"word{i}").ToArray();
        var text = string.Join(" ", words);

        var chunks = TextChunker.Chunk(text, chunkSize: 100, overlapSize: 20);

        // The last 20 words of chunk[0] should appear at the start of chunk[1]
        Assert.True(chunks.Count >= 2);
        var lastWordsOfFirst = chunks[0].Split(' ')[^20..];
        var firstWordsOfSecond = chunks[1].Split(' ')[..20];
        Assert.Equal(lastWordsOfFirst, firstWordsOfSecond);
    }

    [Fact]
    public void Chunk_ExactlyChunkSize_ReturnsSingleChunk()
    {
        var text = string.Join(" ", Enumerable.Range(0, 300).Select(i => $"w{i}"));
        var chunks = TextChunker.Chunk(text, chunkSize: 300, overlapSize: 60);
        Assert.Single(chunks);
    }

    [Fact]
    public void Chunk_DefaultParameters_UseExpectedDefaults()
    {
        Assert.Equal(300, TextChunker.ChunkSize);
        Assert.Equal(60, TextChunker.OverlapSize);
    }

    [Fact]
    public void Chunk_OverlapSizeEqualToChunkSize_Throws()
    {
        Assert.Throws<ArgumentException>(() => TextChunker.Chunk("some text", chunkSize: 10, overlapSize: 10));
    }

    [Fact]
    public void Chunk_AllChunksHaveContent()
    {
        var text = string.Join(" ", Enumerable.Range(0, 500).Select(i => $"word{i}"));
        var chunks = TextChunker.Chunk(text, chunkSize: 100, overlapSize: 20);
        Assert.All(chunks, c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }
}
