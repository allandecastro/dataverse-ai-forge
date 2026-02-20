using DataverseForge.Application.Dtos;

namespace DataverseForge.Application.Contracts;

public interface IAgent
{
    string Name { get; }
    IReadOnlyList<string> HandoffTargets { get; }
    Task<ChatResponse> ChatAsync(string sessionId, string message, CancellationToken cancellationToken = default);
}
