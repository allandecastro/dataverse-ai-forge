using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Owns one <see cref="SqliteRagStore"/> per project, each in its own database file at
/// <c>{rootDir}/{projectId}/rag.db</c>. A project is a session (<c>ProjectState.ProjectId</c>
/// == <c>sessionId</c>), so documents ingested in one project are never searchable from another.
/// Singleton: the per-project stores are cached so the schema check runs once per project.
/// </summary>
public sealed partial class RagStoreProvider(string rootDir)
{
    private readonly ConcurrentDictionary<string, SqliteRagStore> _stores = new();

    public SqliteRagStore GetStore(string projectId) =>
        _stores.GetOrAdd(ToDirectoryName(projectId),
            dir => new SqliteRagStore(Path.Combine(rootDir, dir, "rag.db")));

    /// <summary>
    /// Drops the project's knowledge base. Called when the project (session) is deleted so
    /// its ingested documents don't outlive it.
    /// </summary>
    public void DeleteProject(string projectId)
    {
        var dir = ToDirectoryName(projectId);
        _stores.TryRemove(dir, out _);

        var path = Path.Combine(rootDir, dir);
        if (!Directory.Exists(path))
            return;

        SqliteRagStore.ReleasePooledConnections(Path.Combine(path, "rag.db"));
        Directory.Delete(path, recursive: true);
    }

    /// <summary>
    /// Session ids come from the client — never trust them as a path segment. Safe ids are
    /// used as-is (readable on disk); anything else is hashed.
    /// </summary>
    private static string ToDirectoryName(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new InvalidOperationException(
                "The knowledge base is per project, but no project is active for this request.");

        if (SafeId().IsMatch(projectId))
            return projectId;

        return "h_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projectId)))[..32].ToLowerInvariant();
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SafeId();
}
