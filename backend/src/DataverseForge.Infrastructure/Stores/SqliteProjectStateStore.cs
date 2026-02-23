using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataverseForge.Infrastructure.Stores;

public sealed class SqliteProjectStateStore : IProjectStateStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteProjectStateStore> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
    private bool _schemaEnsured;

    public SqliteProjectStateStore(
        IOptions<PersistenceOptions> options,
        ILogger<SqliteProjectStateStore> logger)
    {
        _connectionString = options.Value.SqliteConnectionString;
        _logger = logger;
    }

    // Visible for testing — allows injecting an in-memory connection string
    internal SqliteProjectStateStore(string connectionString, ILogger<SqliteProjectStateStore> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<ProjectState> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT project_state_json FROM sessions WHERE session_id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        var json = await cmd.ExecuteScalarAsync(cancellationToken) as string;

        if (json is not null)
        {
            var state = JsonSerializer.Deserialize<ProjectState>(json, _jsonOptions);
            if (state is not null) return state;
        }

        // Create default
        var newState = new ProjectState
        {
            ProjectId = sessionId,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        await SaveInternalAsync(connection, sessionId, newState, cancellationToken);

        _logger.LogDebug("Created new session {SessionId}", sessionId);
        return newState;
    }

    public async Task SaveAsync(string sessionId, ProjectState state, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await SaveInternalAsync(connection, sessionId, state, cancellationToken);

        _logger.LogDebug("Saved session {SessionId}", sessionId);
    }

    public async Task<List<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT session_id, project_name, environment_url, solution_name,
                   active_agent, table_count, field_count, created_at,
                   last_activity_at, conversation_summary
            FROM sessions
            ORDER BY last_activity_at DESC
            """;

        var sessions = new List<SessionSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            sessions.Add(new SessionSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                DateTime.Parse(reader.GetString(7)),
                DateTime.Parse(reader.GetString(8)),
                reader.GetString(9)));
        }

        return sessions;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM sessions WHERE session_id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Deleted session {SessionId}", sessionId);
    }

    private static async Task SaveInternalAsync(
        SqliteConnection connection,
        string sessionId,
        ProjectState state,
        CancellationToken cancellationToken)
    {
        var jsonOptions = new JsonSerializerOptions { WriteIndented = false };

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO sessions
                (session_id, project_name, environment_url, solution_name,
                 active_agent, table_count, field_count, created_at,
                 last_activity_at, conversation_summary, project_state_json, conversation_json)
            VALUES
                (@id, @projectName, @envUrl, @solutionName,
                 @activeAgent, @tableCount, @fieldCount, @createdAt,
                 @lastActivity, @summary, @stateJson,
                 COALESCE((SELECT conversation_json FROM sessions WHERE session_id = @id), '[]'))
            """;

        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.Parameters.AddWithValue("@projectName", state.ProjectName);
        cmd.Parameters.AddWithValue("@envUrl", state.EnvironmentUrl);
        cmd.Parameters.AddWithValue("@solutionName", state.SolutionDisplayName);
        cmd.Parameters.AddWithValue("@activeAgent", state.ActiveAgentName);
        cmd.Parameters.AddWithValue("@tableCount", state.Tables.Count);
        cmd.Parameters.AddWithValue("@fieldCount", state.Fields.Count);
        cmd.Parameters.AddWithValue("@createdAt", state.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@lastActivity", state.LastActivityAt.ToString("O"));
        cmd.Parameters.AddWithValue("@summary", state.ConversationSummary);
        cmd.Parameters.AddWithValue("@stateJson", JsonSerializer.Serialize(state, jsonOptions));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaEnsured) return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable WAL mode for concurrent readers
        await using (var walCmd = connection.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode=WAL";
            await walCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sessions (
                session_id TEXT PRIMARY KEY,
                project_name TEXT NOT NULL DEFAULT '',
                environment_url TEXT NOT NULL DEFAULT '',
                solution_name TEXT NOT NULL DEFAULT '',
                active_agent TEXT NOT NULL DEFAULT 'SolutionArchitect',
                table_count INTEGER NOT NULL DEFAULT 0,
                field_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                last_activity_at TEXT NOT NULL,
                conversation_summary TEXT NOT NULL DEFAULT '',
                project_state_json TEXT NOT NULL DEFAULT '{}',
                conversation_json TEXT NOT NULL DEFAULT '[]'
            )
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        _schemaEnsured = true;
        _logger.LogDebug("SQLite schema ensured");
    }
}
