using System.Collections.Concurrent;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
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

    public Task<List<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default)
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
            kvp.Value.LastActivityAt,
            kvp.Value.ConversationSummary)).ToList();

        return Task.FromResult(sessions);
    }

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
