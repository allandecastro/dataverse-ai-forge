using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Infrastructure.Settings;
using Microsoft.Data.Sqlite;

namespace DataverseForge.Infrastructure.Stores;

/// <summary>
/// Per-project connections in their own table of state.db (<c>project_connections</c>),
/// separate from the <c>sessions</c> row so that "Clear session" — which rebuilds the
/// project state — keeps the connection. Secrets (client secret, PAT) are encrypted with
/// the same <see cref="ISecretProtector"/> key as user-settings.json.
/// </summary>
public sealed class SqliteProjectConnectionStore(string connectionString, ISecretProtector protector)
    : IProjectConnectionStore
{
    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaEnsured;

    public async Task<ProjectConnection?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT connection_json FROM project_connections WHERE session_id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        if (await cmd.ExecuteScalarAsync(cancellationToken) is not string json)
            return null;

        var stored = JsonSerializer.Deserialize<ProjectConnection>(json);
        return stored is null ? null : Transform(stored, protector.Unprotect);
    }

    public async Task SaveAsync(string sessionId, ProjectConnection connection, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var json = JsonSerializer.Serialize(Transform(connection, protector.Protect));

        await using var db = new SqliteConnection(connectionString);
        await db.OpenAsync(cancellationToken);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO project_connections (session_id, connection_json, updated_at)
            VALUES (@id, @json, @now)
            ON CONFLICT(session_id) DO UPDATE SET
                connection_json = excluded.connection_json,
                updated_at      = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@id", sessionId);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM project_connections WHERE session_id = @id";
        cmd.Parameters.AddWithValue("@id", sessionId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Applies <paramref name="secret"/> (protect or unprotect) to every secret field.</summary>
    private static ProjectConnection Transform(ProjectConnection c, Func<string, string> secret) => c with
    {
        Dataverse = c.Dataverse is null ? null : c.Dataverse with { ClientSecret = secret(c.Dataverse.ClientSecret ?? "") },
        AzureDevOps = c.AzureDevOps is null ? null : c.AzureDevOps with
        {
            PersonalAccessToken = secret(c.AzureDevOps.PersonalAccessToken ?? ""),
        },
    };

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaEnsured) return;
        await _schemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_schemaEnsured) return;

            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS project_connections (
                    session_id      TEXT PRIMARY KEY,
                    connection_json TEXT NOT NULL,
                    updated_at      TEXT NOT NULL
                )
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _schemaEnsured = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }
}

/// <summary>In-memory variant for the InMemory persistence provider and tests.</summary>
public sealed class InMemoryProjectConnectionStore : IProjectConnectionStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ProjectConnection> _connections = new();

    public Task<ProjectConnection?> GetAsync(string sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_connections.TryGetValue(sessionId, out var c) ? c : null);

    public Task SaveAsync(string sessionId, ProjectConnection connection, CancellationToken cancellationToken = default)
    {
        _connections[sessionId] = connection;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _connections.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
