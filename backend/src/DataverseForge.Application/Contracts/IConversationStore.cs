using Microsoft.Extensions.AI;

namespace DataverseForge.Application.Contracts;

public interface IConversationStore
{
    Task<List<ChatMessage>> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveAsync(string sessionId, List<ChatMessage> messages, CancellationToken cancellationToken = default);
}
