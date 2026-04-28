using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Creates an unmanaged Dataverse solution and updates ProjectState with the
/// solution metadata so downstream agents and the sidebar reflect the choice.
/// </summary>
public class CreateSolutionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<CreateSolutionTool> logger)
{
    [Description("Create a new unmanaged Dataverse solution. After creating, the solution becomes "
        + "the active solution for the rest of the session — all subsequent components will be "
        + "added to it. Pass the publisherId from get_publishers. The uniqueName must be PascalCase, "
        + "no spaces, no special chars (e.g., 'FeedbackTraining').")]
    public async Task<string> CreateSolutionAsync(
        [Description("Unique solution name in PascalCase, no spaces (e.g., 'FeedbackTraining')")] string uniqueName,
        [Description("Friendly display name for the solution (e.g., 'Feedback Training')")] string friendlyName,
        [Description("Brief description of what the solution contains")] string description,
        [Description("Solution version, semver format (e.g., '1.0.0.0')")] string version,
        [Description("Publisher GUID from get_publishers — pick one based on user choice")] Guid publisherId,
        [Description("Publisher display name (for state tracking, e.g., 'Allan De Castro')")] string publisherDisplayName,
        [Description("Publisher customization prefix (for state tracking, e.g., 'adc')")] string publisherPrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var solutionId = await dataverseClient.CreateSolutionAsync(
                uniqueName, friendlyName, description, version, publisherId, cancellationToken);

            // Update ProjectState so the sidebar + future agents see the new solution
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.SolutionId = solutionId;
            state.SolutionUniqueName = uniqueName;
            state.SolutionDisplayName = friendlyName;
            state.SolutionVersion = version;
            state.PublisherId = publisherId;
            state.PublisherDisplayName = publisherDisplayName;
            state.PublisherPrefix = publisherPrefix;
            state.LastActivityAt = DateTime.UtcNow;
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("Created solution {Solution} ({SolutionId}) for session {SessionId}",
                uniqueName, solutionId, sessionContext.SessionId);

            return $"Solution '{friendlyName}' (uniqueName: {uniqueName}, prefix: {publisherPrefix}_, "
                + $"version: {version}) created successfully. SolutionId: {solutionId}. "
                + "It is now the active solution.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create solution {UniqueName}", uniqueName);
            return $"ERROR: Failed to create solution: {ex.Message}";
        }
    }
}
