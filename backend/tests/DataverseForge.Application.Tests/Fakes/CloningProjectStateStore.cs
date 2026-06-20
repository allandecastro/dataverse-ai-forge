using System.Collections.Concurrent;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="IProjectStateStore"/> that models a *persistent*
/// store: every <see cref="GetOrCreateAsync"/> hands back an independent
/// snapshot (deep clone) of the persisted state, and <see cref="SaveAsync"/>
/// replaces the backing copy. This is the realistic adversary for the F-23
/// stale-state write race — a snapshot read at the top of a tool method is a
/// DIFFERENT object from one a concurrent writer persisted during a long
/// Dataverse call, so a stale <c>SaveAsync</c> silently clobbers it.
///
/// The production <c>InMemoryProjectStateStore</c> happens to return the same
/// reference today, which masks the race; <see cref="ApplyAndSaveAsync"/>
/// (re-read immediately before save) is the contract that survives a cloning
/// store, and this fake is what proves it.
/// </summary>
public sealed class CloningProjectStateStore : IProjectStateStore
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public int SaveCount { get; private set; }

    public Task<ProjectState> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var json = _store.GetOrAdd(sessionId, _ => Serialize(new ProjectState { ProjectId = sessionId }));
        return Task.FromResult(Deserialize(json));
    }

    public Task SaveAsync(string sessionId, ProjectState state, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        _store[sessionId] = Serialize(state);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates a concurrent flow that read, mutated, and persisted the
    /// session DURING a long Dataverse call (read-mutate-save against the
    /// backing store, independent of any snapshot a tool is holding).
    /// </summary>
    public void SimulateConcurrentWrite(string sessionId, Action<ProjectState> mutate)
    {
        var fresh = GetOrCreateAsync(sessionId).GetAwaiter().GetResult();
        mutate(fresh);
        SaveAsync(sessionId, fresh).GetAwaiter().GetResult();
    }

    public Task<List<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<SessionSummary>());

    public Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _store.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private static string Serialize(ProjectState state) => JsonSerializer.Serialize(state);

    private static ProjectState Deserialize(string json) =>
        JsonSerializer.Deserialize<ProjectState>(json)!;
}
