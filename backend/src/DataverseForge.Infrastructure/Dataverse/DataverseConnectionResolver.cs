using DataverseForge.Application.Contracts;

namespace DataverseForge.Infrastructure.Dataverse;

/// <summary>
/// Resolves the Dataverse connection of the project the current request is working on.
/// Scoped. The session id is read on every call, not at construction: the orchestrator
/// builds the agents (and their Dataverse clients) before it sets
/// <see cref="ISessionContext.SessionId"/> for the turn.
/// </summary>
public sealed class DataverseConnectionResolver(ISessionContext sessionContext, IProjectConnectionStore store)
{
    public async Task<DataverseConnection> GetRequiredAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = sessionContext.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ProjectConnectionMissingException(
                "No project is active for this request, so there is no Dataverse connection to use.");

        var connection = (await store.GetAsync(sessionId, cancellationToken))?.Dataverse;
        if (connection is null || !connection.IsConfigured)
            throw new ProjectConnectionMissingException(
                "This project has no Dataverse connection configured. Ask the user to open the project's " +
                "connection settings (Settings → This project) and enter the environment URL and credentials. " +
                "Each project uses its own Dataverse environment.");

        return connection;
    }
}
