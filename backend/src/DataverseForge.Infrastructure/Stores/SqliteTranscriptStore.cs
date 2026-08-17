using DataverseForge.Application.Contracts;
using Microsoft.Data.Sqlite;

namespace DataverseForge.Infrastructure.Stores;

/// <summary>
/// Durable <see cref="ITranscriptStore"/> — stores the UI transcript in the
/// <c>transcript_json</c> column of the shared <c>sessions</c> table, so it lives
/// alongside project state in <c>state.db</c>. Depends on the concrete
/// <see cref="SqliteProjectStateStore"/> for schema creation (same DB file).
/// </summary>
public sealed class SqliteTranscriptStore : ITranscriptStore
{
    private readonly string _connectionString;
    private readonly SqliteProjectStateStore _stateStore;

    public SqliteTranscriptStore(string connectionString, SqliteProjectStateStore stateStore)
    {
        _connectionString = connectionString;
        _stateStore = stateStore;
    }

    public async Task<string> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await _stateStore.EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT transcript_json FROM sessions WHERE session_id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        var json = await cmd.ExecuteScalarAsync(cancellationToken) as string;
        return string.IsNullOrEmpty(json) ? "[]" : json;
    }

    public async Task SaveAsync(string sessionId, string transcriptJson, CancellationToken cancellationToken = default)
    {
        await _stateStore.EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Upsert — the row may not exist yet (state saved separately).
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (session_id, created_at, last_activity_at, transcript_json)
            VALUES (@id, @now, @now, @json)
            ON CONFLICT(session_id) DO UPDATE SET transcript_json = @json
            """;
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@json", transcriptJson);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
