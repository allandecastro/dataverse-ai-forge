using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Stores;

public sealed class SqliteProjectStateStore : IProjectStateStore
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteProjectStateStore> _logger;

    // The persisted blob holds BUSINESS state only. OperationStack (the undo
    // stack) and AuditLog (the activity/diagnostic trail) are session-scoped,
    // not durable — and would otherwise grow the blob without bound. We drop
    // them from the DB serialization ONLY; they still flow to the frontend via
    // the SSE STATE_SNAPSHOT, so the live Activity panel + undo keep working.
    // On reload they come back empty (their property initializers), which is the
    // intended "undo / activity history resets per session" behaviour.
    private static readonly JsonSerializerOptions _dbJsonOptions = new()
    {
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static typeInfo =>
                {
                    if (typeInfo.Type != typeof(ProjectState)) return;
                    for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
                    {
                        if (typeInfo.Properties[i].Name is "OperationStack" or "AuditLog")
                            typeInfo.Properties.RemoveAt(i);
                    }
                }
            }
        }
    };

    // Guards the one-time schema creation so a concurrent first-call can't
    // double-run the CREATE TABLE / WAL PRAGMA (idempotent, but keep it clean).
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaEnsured;

    /// <param name="connectionString">e.g. <c>Data Source=/path/to/state.db</c> — wired
    /// from <c>{writable data dir}/state.db</c> in <c>DependencyInjection.AddStores</c>.</param>
    public SqliteProjectStateStore(string connectionString, ILogger<SqliteProjectStateStore> logger)
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
            var state = JsonSerializer.Deserialize<ProjectState>(json, _dbJsonOptions);
            if (state is not null) return state;
        }

        // Not found → return a fresh in-memory default WITHOUT inserting a row.
        // A row is written only on the first real SaveAsync (a named project via
        // POST /api/sessions, or a tool persisting actual work), so ephemeral
        // sessions that never save leave no phantom empty-name rows behind.
        return new ProjectState
        {
            ProjectId = sessionId,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };
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
                   last_activity_at
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
                DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
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
        await using var cmd = connection.CreateCommand();
        // conversation_summary column is kept in the schema for backward compatibility
        // with existing SQLite files, but always written as ''. The Domain property was
        // removed; nothing in the runtime populates it anymore.
        cmd.CommandText = """
            INSERT OR REPLACE INTO sessions
                (session_id, project_name, environment_url, solution_name,
                 active_agent, table_count, field_count, created_at,
                 last_activity_at, conversation_summary, project_state_json, conversation_json)
            VALUES
                (@id, @projectName, @envUrl, @solutionName,
                 @activeAgent, @tableCount, @fieldCount, @createdAt,
                 @lastActivity, '', @stateJson,
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
        cmd.Parameters.AddWithValue("@stateJson", JsonSerializer.Serialize(state, _dbJsonOptions));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        if (_schemaEnsured) return;
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_schemaEnsured) return;
            await EnsureSchemaCoreAsync(cancellationToken);
            _schemaEnsured = true;
            _logger.LogDebug("SQLite schema ensured");
        }
        finally
        {
            _schemaGate.Release();
        }
    }

    private async Task EnsureSchemaCoreAsync(CancellationToken cancellationToken)
    {
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
    }
}
