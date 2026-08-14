using System.Runtime.CompilerServices;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Rag;
using Microsoft.Data.Sqlite;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// SQLite-backed RAG store with in-memory cosine-similarity search.
///
/// <para><b>Schema</b>:
/// <list type="bullet">
///   <item><c>rag_sources</c> — one row per registered source (id, name, type, path, priority, last_ingested_at)</item>
///   <item><c>rag_chunks</c> — one row per chunk (source_id, file_path, chunk_index, content, embedding BLOB, file_last_modified)</item>
/// </list></para>
///
/// <para><b>Similarity</b>: embeddings are stored as raw IEEE 754 float arrays (4 bytes/dim).
/// Search loads all candidate vectors into memory and computes cosine similarity in C#.
/// This is efficient enough for project-KB scale (up to ~50K chunks / ~300 MB for 1536-dim embeddings).
/// At larger scale, swap in a proper ANN index (sqlite-vec, hnswlib, etc.).</para>
/// </summary>
public sealed class SqliteRagStore : IRagStore, IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteRagStore(string databasePath)
    {
        var dir = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        EnsureSchema();
    }

    // ─── Schema ──────────────────────────────────────────────────────────────

    private void EnsureSchema()
    {
        Execute(@"
            CREATE TABLE IF NOT EXISTS rag_sources (
                id               TEXT PRIMARY KEY,
                name             TEXT NOT NULL,
                source_type      TEXT NOT NULL,
                path             TEXT,
                category         TEXT NOT NULL DEFAULT 'Internal',
                last_ingested_at TEXT
            );

            CREATE TABLE IF NOT EXISTS rag_chunks (
                id               INTEGER PRIMARY KEY AUTOINCREMENT,
                source_id        TEXT NOT NULL,
                file_path        TEXT NOT NULL,
                chunk_index      INTEGER NOT NULL,
                content          TEXT NOT NULL,
                embedding        BLOB NOT NULL,
                file_last_modified TEXT NOT NULL,
                UNIQUE(source_id, file_path, chunk_index)
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_source_file
                ON rag_chunks(source_id, file_path);
        ");

        // Migration: add category column if upgrading from a DB that had the old priority column.
        // SQLite does not support IF NOT EXISTS on ALTER TABLE; swallow the duplicate-column error.
        try
        {
            Execute("ALTER TABLE rag_sources ADD COLUMN category TEXT NOT NULL DEFAULT 'Internal'");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Column already exists — nothing to do.
        }
    }

    // ─── Sources ─────────────────────────────────────────────────────────────

    public Task UpsertSourceAsync(string sourceId, string sourceName, string sourceType, string? path, SourceCategory category, CancellationToken cancellationToken = default)
    {
        Execute(@"
            INSERT INTO rag_sources (id, name, source_type, path, category, last_ingested_at)
            VALUES (@id, @name, @type, @path, @category, NULL)
            ON CONFLICT(id) DO UPDATE SET
                name        = excluded.name,
                source_type = excluded.source_type,
                path        = excluded.path,
                category    = excluded.category",
            ("@id", sourceId), ("@name", sourceName), ("@type", sourceType),
            ("@path", (object?)path ?? DBNull.Value), ("@category", category.ToString()));
        return Task.CompletedTask;
    }

    public Task MarkIngestedAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        Execute("UPDATE rag_sources SET last_ingested_at = @now WHERE id = @id",
            ("@id", sourceId), ("@now", DateTimeOffset.UtcNow.ToString("O")));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SourceInfo>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<SourceInfo>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, source_type, path, category, last_ingested_at FROM rag_sources ORDER BY id ASC";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var categoryStr = reader.GetString(4);
            var category = Enum.TryParse<SourceCategory>(categoryStr, out var parsed)
                ? parsed
                : SourceCategory.Internal;
            results.Add(new SourceInfo(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                category,
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5))));
        }
        return results;
    }

    public Task DeleteSourceAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        Execute("DELETE FROM rag_chunks WHERE source_id = @id", ("@id", sourceId));
        Execute("DELETE FROM rag_sources WHERE id = @id", ("@id", sourceId));
        return Task.CompletedTask;
    }

    // ─── Chunks ──────────────────────────────────────────────────────────────

    public Task UpsertChunksAsync(string sourceId, string filePath, DateTimeOffset lastModified,
        IReadOnlyList<(string Text, ReadOnlyMemory<float> Embedding)> chunks, CancellationToken cancellationToken = default)
    {
        // Delete all existing chunks for this file before inserting the new ones.
        Execute("DELETE FROM rag_chunks WHERE source_id = @sid AND file_path = @fp",
            ("@sid", sourceId), ("@fp", filePath));

        if (chunks.Count == 0)
            return Task.CompletedTask;

        using var tx = _connection.BeginTransaction();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO rag_chunks (source_id, file_path, chunk_index, content, embedding, file_last_modified)
            VALUES (@sid, @fp, @idx, @content, @emb, @lm)";

        var pSid = cmd.Parameters.Add("@sid", SqliteType.Text); pSid.Value = sourceId;
        var pFp = cmd.Parameters.Add("@fp", SqliteType.Text); pFp.Value = filePath;
        var pIdx = cmd.Parameters.Add("@idx", SqliteType.Integer);
        var pContent = cmd.Parameters.Add("@content", SqliteType.Text);
        var pEmb = cmd.Parameters.Add("@emb", SqliteType.Blob);
        var pLm = cmd.Parameters.Add("@lm", SqliteType.Text); pLm.Value = lastModified.ToString("O");

        for (var i = 0; i < chunks.Count; i++)
        {
            pIdx.Value = i;
            pContent.Value = chunks[i].Text;
            pEmb.Value = EmbeddingToBytes(chunks[i].Embedding);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return Task.CompletedTask;
    }

    public async Task<DateTimeOffset?> GetFileLastModifiedAsync(string sourceId, string filePath, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT file_last_modified FROM rag_chunks WHERE source_id = @sid AND file_path = @fp LIMIT 1";
        cmd.Parameters.AddWithValue("@sid", sourceId);
        cmd.Parameters.AddWithValue("@fp", filePath);
        var val = await cmd.ExecuteScalarAsync(cancellationToken);
        return val is string s ? DateTimeOffset.Parse(s) : null;
    }

    public async Task<IReadOnlyList<string>> GetIndexedFilePathsAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var paths = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT file_path FROM rag_chunks WHERE source_id = @sid";
        cmd.Parameters.AddWithValue("@sid", sourceId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            paths.Add(reader.GetString(0));
        return paths;
    }

    public async Task<IReadOnlyList<ChunkInfo>> GetAllChunksAsync(string sourceId, CancellationToken cancellationToken = default)
    {
        var chunks = new List<ChunkInfo>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT file_path, chunk_index, content, embedding
            FROM rag_chunks
            WHERE source_id = @sid
            ORDER BY file_path, chunk_index";
        cmd.Parameters.AddWithValue("@sid", sourceId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var embBytes = (byte[])reader.GetValue(3);
            chunks.Add(new ChunkInfo(
                sourceId,
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                new ReadOnlyMemory<float>(BytesToEmbedding(embBytes))));
        }
        return chunks;
    }

    // ─── Search ──────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RagResult>> SearchAsync(ReadOnlyMemory<float> queryEmbedding,
        int topK, IReadOnlyList<string>? sourceIds = null, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();

        // Build dynamic WHERE clause
        var whereClause = sourceIds is { Count: > 0 }
            ? "WHERE c.source_id IN (" + string.Join(",", sourceIds.Select((_, i) => $"@sid{i}")) + ")"
            : "";

        cmd.CommandText = $@"
            SELECT c.source_id, s.name, c.file_path, c.chunk_index, c.content, c.embedding
            FROM rag_chunks c
            JOIN rag_sources s ON s.id = c.source_id
            {whereClause}";

        if (sourceIds is { Count: > 0 })
        {
            for (var i = 0; i < sourceIds.Count; i++)
                cmd.Parameters.AddWithValue($"@sid{i}", sourceIds[i]);
        }

        var queryVec = queryEmbedding.ToArray(); // float[] — safe to capture across await boundaries
        var scored = new List<(RagResult Result, float Score)>();

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var embBytes = (byte[])reader.GetValue(5);
            var embVec = BytesToEmbedding(embBytes);
            var score = CosineSimilarity(queryVec, embVec);
            scored.Add((new RagResult(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                score), score));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => x.Result with { Score = x.Score })
            .ToList();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static byte[] EmbeddingToBytes(ReadOnlyMemory<float> embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] BytesToEmbedding(byte[] bytes)
    {
        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;

        var dot = 0f;
        var magA = 0f;
        var magB = 0f;

        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denom < 1e-9f ? 0f : dot / denom;
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();
}
