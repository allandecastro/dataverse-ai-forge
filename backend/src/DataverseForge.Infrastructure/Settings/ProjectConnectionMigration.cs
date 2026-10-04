using DataverseForge.Application.Contracts;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.Settings;

/// <summary>
/// One-time upgrade from the global Dataverse / Azure DevOps connection to per-project
/// connections. Every existing project that has no connection yet gets a copy of the
/// legacy global one (those projects were built against it), then the global sections
/// are removed from user-settings.json — so it never runs again, and projects created
/// afterwards start with an empty connection.
///
/// With no projects at all there is nobody to hand the connection to; it is kept and
/// the migration retries on the next start.
/// </summary>
public static class ProjectConnectionMigration
{
    public static async Task RunAsync(
        UserSettingsService settings,
        IProjectStateStore projects,
        IProjectConnectionStore connections,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var legacy = await settings.ReadLegacyConnectionAsync();
        if (legacy is null) return;

        var sessions = await projects.ListSessionsAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            logger.LogInformation("Legacy global connection found but no projects yet; keeping it for the next start");
            return;
        }

        var migrated = 0;
        foreach (var session in sessions)
        {
            if (await connections.GetAsync(session.SessionId, cancellationToken) is not null)
                continue;
            await connections.SaveAsync(session.SessionId, legacy, cancellationToken);
            migrated++;
        }

        await settings.RemoveLegacyConnectionAsync();
        logger.LogInformation(
            "Migrated the global Dataverse/Azure DevOps connection to {Count} existing project(s); removed it from user settings",
            migrated);
    }
}
