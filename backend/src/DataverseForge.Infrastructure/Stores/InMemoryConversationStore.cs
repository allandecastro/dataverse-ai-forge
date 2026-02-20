using System.Collections.Concurrent;
using DataverseForge.Application.Contracts;
using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.Stores;

public class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _store = new();

    public Task<List<ChatMessage>> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var history = _store.GetOrAdd(sessionId, _ => []);
        return Task.FromResult(history);
    }
}
