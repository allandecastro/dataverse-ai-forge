using System.Text.Json;
using DataverseForge.Application.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Stores;

public sealed class SqliteConversationStore : IConversationStore
{
    private readonly string _connectionString;
    private readonly SqliteProjectStateStore _stateStore;
    private readonly ILogger<SqliteConversationStore> _logger;

    public SqliteConversationStore(
        IOptions<PersistenceOptions> options,
        SqliteProjectStateStore stateStore,
        ILogger<SqliteConversationStore> logger)
    {
        _connectionString = options.Value.SqliteConnectionString;
        _stateStore = stateStore;
        _logger = logger;
    }

    // Visible for testing
    internal SqliteConversationStore(
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

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET conversation_json = @json WHERE session_id = @id";
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@id", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogDebug("Saved {MessageCount} messages for session {SessionId}", messages.Count, sessionId);
    }

    private record StoredMessage(string Role, string Content);
}
