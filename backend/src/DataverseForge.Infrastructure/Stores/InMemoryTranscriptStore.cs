using System.Collections.Concurrent;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Infrastructure.Stores;

/// <summary>Ephemeral <see cref="ITranscriptStore"/> — transcripts are lost on
/// restart. Used under the InMemory persistence provider (and by tests).</summary>
public sealed class InMemoryTranscriptStore : ITranscriptStore
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Task<string> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_store.TryGetValue(sessionId, out var t) ? t : "[]");

    public Task SaveAsync(string sessionId, string transcriptJson, CancellationToken cancellationToken = default)
    {
        _store[sessionId] = transcriptJson;
        return Task.CompletedTask;
    }
}
