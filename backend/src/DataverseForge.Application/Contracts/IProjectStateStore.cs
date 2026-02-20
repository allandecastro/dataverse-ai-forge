using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Contracts;

public interface IProjectStateStore
{
    Task<ProjectState> GetOrCreateAsync(string sessionId, CancellationToken cancellationToken = default);
    Task SaveAsync(string sessionId, ProjectState state, CancellationToken cancellationToken = default);
}
