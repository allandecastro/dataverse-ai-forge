using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Marks an existing Dataverse solution as the active solution for the session.
/// No Dataverse write — purely updates ProjectState so the sidebar + downstream
/// agents know which solution to add components to.
/// </summary>
public class SelectSolutionTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<SelectSolutionTool> logger)
{
    [Description("Mark an EXISTING Dataverse solution as the active solution for this session. "
        + "Call this AFTER the user picks an existing solution from get_solutions_list (do NOT call "
        + "for new solutions — use create_solution instead). All fields come from the get_solutions_list "
        + "result. Updates ProjectState so the sidebar shows the selected solution + prefix and downstream "
        + "agents know where to add components.")]
    public async Task<string> SelectSolutionAsync(
        [Description("Solution GUID from get_solutions_list (SolutionId field)")] Guid solutionId,
        [Description("Solution unique name (UniqueName field)")] string uniqueName,
        [Description("Solution display/friendly name (DisplayName field)")] string displayName,
        [Description("Solution version (Version field)")] string version,
        [Description("Publisher GUID (PublisherId field)")] Guid publisherId,
        [Description("Publisher display name (PublisherName field)")] string publisherDisplayName,
        [Description("Publisher customization prefix (PublisherPrefix field) — e.g. 'adc'")] string publisherPrefix,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.SolutionId = solutionId;
            state.SolutionUniqueName = uniqueName;
            state.SolutionDisplayName = displayName;
            state.SolutionVersion = version;
            state.PublisherId = publisherId;
            state.PublisherDisplayName = publisherDisplayName;
            state.PublisherPrefix = publisherPrefix;
            state.SolutionConfirmed = true;
            state.LastActivityAt = DateTime.UtcNow;
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("Selected existing solution {Solution} ({SolutionId}) for session {SessionId}",
                uniqueName, solutionId, sessionContext.SessionId);

            return $"Active solution set to '{displayName}' (uniqueName: {uniqueName}, prefix: {publisherPrefix}_, "
                + $"version: {version}). All subsequent components will be added to this solution.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to select solution {UniqueName}", uniqueName);
            return $"ERROR: Failed to select solution: {ex.Message}";
        }
    }
}
