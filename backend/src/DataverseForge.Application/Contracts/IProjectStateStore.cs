using DataverseForge.Application.Dtos;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Contracts;

public interface IProjectStateStore
{
    Task<ProjectState> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveAsync(string sessionId, ProjectState state, CancellationToken cancellationToken = default);
    Task<List<SessionSummary>> ListSessionsAsync(CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}
