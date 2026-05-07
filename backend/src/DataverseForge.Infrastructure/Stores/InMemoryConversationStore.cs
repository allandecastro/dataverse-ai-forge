using DataverseForge.Application.Contracts;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.Stores;

/// <summary>
/// In-memory <see cref="IConversationStore"/> with bounded capacity (F-42).
/// Mirrors <see cref="InMemoryProjectStateStore"/>'s LRU shape — a chat
/// history per session is potentially heavy (several MB after a long
/// session), so the unbounded dictionary was a real leak.
/// </summary>
public class InMemoryConversationStore : IConversationStore
{
    public const int MaxSessions = 100;

    private readonly OrderedDictionary<string, List<ChatMessage>> _store = new();
    private readonly object _gate = new();

    public Task<List<ChatMessage>> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_store.TryGetValue(sessionId, out var existing))
            {
                MoveToBack(sessionId);
                return Task.FromResult(existing);
            }
            var fresh = new List<ChatMessage>();
            _store[sessionId] = fresh;
            EvictIfOverCapacity();
            return Task.FromResult(fresh);
        }
    }

    public Task SaveAsync(string sessionId, List<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _store[sessionId] = messages;
            MoveToBack(sessionId);
            EvictIfOverCapacity();
        }
        return Task.CompletedTask;
    }

    private void MoveToBack(string sessionId)
    {
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
            _store.RemoveAt(0);
        }
    }
}
