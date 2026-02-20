using System.Collections.Concurrent;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Infrastructure.Stores;

public class InMemoryProjectStateStore : IProjectStateStore
{
    private readonly ConcurrentDictionary<string, ProjectState> _store = new();

    public Task<ProjectState> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var state = _store.GetOrAdd(sessionId, _ => new ProjectState
        {
            ProjectId = sessionId,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        });

        return Task.FromResult(state);
    }

    public Task SaveAsync(string sessionId, ProjectState state, CancellationToken cancellationToken = default)
    {
        _store[sessionId] = state;
        return Task.CompletedTask;
    }
}
