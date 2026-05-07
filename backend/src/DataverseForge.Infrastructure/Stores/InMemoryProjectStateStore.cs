using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain.Models;

namespace DataverseForge.Infrastructure.Stores;

/// <summary>
/// In-memory <see cref="IProjectStateStore"/> with bounded capacity (F-42).
///
/// Earlier this was a plain <c>ConcurrentDictionary</c> with no eviction —
/// every new session leaked a <c>ProjectState</c> (with its growing
/// <c>AuditLog</c>, <c>OperationStack</c>, <c>PendingPlans</c>, etc.) for
/// the lifetime of the process. With SQLite removed, in-memory is now the
/// only path; that latent leak became real.
///
/// Now backed by <see cref="OrderedDictionary{TKey,TValue}"/> with simple
/// access-order LRU eviction at <see cref="MaxSessions"/>. On every read
/// or write the touched key is moved to the back; when the count would
/// exceed the cap we evict the front entry. Single coarse lock — sessions
/// are agent-driven and traffic is bursty, not throughput-critical.
/// </summary>
public class InMemoryProjectStateStore : IProjectStateStore
{
    public const int MaxSessions = 100;

    private readonly OrderedDictionary<string, ProjectState> _store = new();
    private readonly object _gate = new();

    public Task<ProjectState> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_store.TryGetValue(sessionId, out var existing))
            {
                MoveToBack(sessionId);
                return Task.FromResult(existing);
            }

            var fresh = new ProjectState
            {
                ProjectId = sessionId,
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
            _store[sessionId] = fresh;
            EvictIfOverCapacity();
            return Task.FromResult(fresh);
        }
    }

    public Task SaveAsync(string sessionId, ProjectState state, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _store[sessionId] = state;
            MoveToBack(sessionId);
            EvictIfOverCapacity();
        }
        return Task.CompletedTask;
    }

    public Task<List<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var sessions = _store.Select(kvp => new SessionSummary(
                kvp.Key,
                kvp.Value.ProjectName,
                kvp.Value.EnvironmentUrl,
                kvp.Value.SolutionDisplayName,
                kvp.Value.ActiveAgentName,
                kvp.Value.Tables.Count,
                kvp.Value.Fields.Count,
                kvp.Value.CreatedAt,
                kvp.Value.LastActivityAt)).ToList();
            return Task.FromResult(sessions);
        }
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _store.Remove(sessionId);
        }
        return Task.CompletedTask;
    }

    private void MoveToBack(string sessionId)
    {
        // OrderedDictionary doesn't have MoveToBack — Remove + Add appends.
        // No-op if the key doesn't exist (TryGetValue caller has already
        // confirmed presence on read paths; on Save the indexer assignment
        // above adds it).
        if (_store.TryGetValue(sessionId, out var value))
        {
            _store.Remove(sessionId);
            _store[sessionId] = value;
        }
    }

    private void EvictIfOverCapacity()
    {
        while (_store.Count > MaxSessions)
        {
            // RemoveAt(0) drops the front (least-recently-touched) entry.
            _store.RemoveAt(0);
        }
    }
}
