using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
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
        var resolvedPublisherId = publisherId;
        try
        {
            Guid solutionId;
            try
            {
                solutionId = await dataverseClient.CreateSolutionAsync(
                    uniqueName, friendlyName, description, version, resolvedPublisherId, cancellationToken);
            }
            catch (Exception firstAttemptEx) when (LooksLikePublisherNotFound(firstAttemptEx))
            {
                // The agent occasionally hands us a hallucinated/stale publisher GUID. Re-resolve
                // by displayName + prefix from the live publisher list and retry once.
                logger.LogWarning(firstAttemptEx,
                    "CreateSolution failed with what looks like a publisher-not-found error (publisherId={PublisherId}); attempting to re-resolve by displayName='{DisplayName}' / prefix='{Prefix}'",
                    publisherId, publisherDisplayName, publisherPrefix);

                var publishers = await dataverseClient.GetPublishersAsync(cancellationToken);
                var match = publishers.FirstOrDefault(p =>
                        !string.IsNullOrWhiteSpace(publisherPrefix) &&
                        string.Equals(p.Prefix, publisherPrefix, StringComparison.OrdinalIgnoreCase))
                    ?? publishers.FirstOrDefault(p =>
                        !string.IsNullOrWhiteSpace(publisherDisplayName) &&
                        string.Equals(p.DisplayName, publisherDisplayName, StringComparison.OrdinalIgnoreCase));

                if (match is null || match.PublisherId == Guid.Empty)
                {
                    return "ERROR: Failed to create solution — the publisherId I was given does not exist in this environment, "
                        + $"and no publisher matched displayName='{publisherDisplayName}' or prefix='{publisherPrefix}'. "
                        + "Call get_publishers and pass a valid publisherId verbatim.";
                }

                logger.LogInformation("Re-resolved publisher to {ResolvedId} (was {OriginalId})", match.PublisherId, publisherId);
                resolvedPublisherId = match.PublisherId;
                solutionId = await dataverseClient.CreateSolutionAsync(
                    uniqueName, friendlyName, description, version, resolvedPublisherId, cancellationToken);
            }

            // Update ProjectState so the sidebar + future agents see the new solution
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.SolutionId = solutionId;
            state.SolutionUniqueName = uniqueName;
            state.SolutionDisplayName = friendlyName;
            state.SolutionVersion = version;
            state.PublisherId = resolvedPublisherId;
            state.PublisherDisplayName = publisherDisplayName;
            state.PublisherPrefix = publisherPrefix;
            state.LastActivityAt = DateTime.UtcNow;

            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                ToolName = "CreateSolution",
                Parameters = new { uniqueName, friendlyName, publisherId = resolvedPublisherId },
                Result = new { solutionId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeleteSolution",
                    Parameters = new { solutionId },
                    Warning = "Deleting a solution removes the solution container; components inside remain in the environment but lose their solution membership.",
                    RiskLevel = RiskLevel.High
                }
            });
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                Action = "CreateSolution",
                Detail = $"Created solution '{friendlyName}' ({uniqueName})",
                Success = true
            });

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

    private static bool LooksLikePublisherNotFound(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("publisher", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("404", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }
}
