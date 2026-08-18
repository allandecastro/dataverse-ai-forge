using System.Text.Json;
using DataverseForge.Application.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Stores;

public sealed class SqliteConversationStore : IConversationStore
{
    private readonly string _connectionString;
    private readonly SqliteProjectStateStore _stateStore;
    private readonly ILogger<SqliteConversationStore> _logger;

    /// <param name="connectionString">Same <c>state.db</c> as the project-state store —
    /// conversation lives in the <c>conversation_json</c> column of the <c>sessions</c> table.</param>
    public SqliteConversationStore(
        string connectionString,
        SqliteProjectStateStore stateStore,
        ILogger<SqliteConversationStore> logger)
    {
        _connectionString = connectionString;
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task<List<ChatMessage>> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        // Ensure schema is created (via the shared state store)
        await _stateStore.EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT conversation_json FROM sessions WHERE session_id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        var json = await cmd.ExecuteScalarAsync(cancellationToken) as string;

        if (json is not null && json != "[]")
        {
            var stored = JsonSerializer.Deserialize<List<StoredMessage>>(json);
            if (stored is not null)
            {
                return stored.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)).ToList();
            }
        }

        return [];
    }

    public async Task SaveAsync(string sessionId, List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        await _stateStore.EnsureSchemaAsync(cancellationToken);

        // Convert to simplified format to avoid AIContent polymorphism issues
        var stored = messages.Select(m => new StoredMessage(
            m.Role.Value,
            m.Text ?? string.Empty)).ToList();

        var json = JsonSerializer.Serialize(stored);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Upsert: the row may not exist yet (the project-state store no longer
        // inserts on read), so a plain UPDATE could silently drop messages.
        // Insert a minimal row if absent, otherwise just update conversation_json.
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sessions (session_id, created_at, last_activity_at, conversation_json)
            VALUES (@id, @now, @now, @json)
            ON CONFLICT(session_id) DO UPDATE SET conversation_json = @json
            """;
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@json", json);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Saved {MessageCount} messages for session {SessionId}", messages.Count, sessionId);
    }

    private record StoredMessage(string Role, string Content);
}
