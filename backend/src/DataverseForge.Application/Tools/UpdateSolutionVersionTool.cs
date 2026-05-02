using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Updates the version of the active solution. Versions follow semver format
/// (Major.Minor.Build.Revision, e.g. "1.2.0.0"). Bump the version when shipping
/// a new release of the solution.
/// </summary>
public class UpdateSolutionVersionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<UpdateSolutionVersionTool> logger)
{
    [Description("Update the version of the active solution to a new value. "
        + "Version must be in semver format Major.Minor.Build.Revision (e.g. '1.2.0.0'). "
        + "The new version must be greater than or equal to the current version — Dataverse "
        + "rejects downgrades. The active solution is read from project state, so the user "
        + "doesn't need to specify which solution.")]
    public async Task<string> UpdateSolutionVersionAsync(
        [Description("New version string, semver Major.Minor.Build.Revision (e.g. '1.1.0.0')")] string newVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(newVersion))
            return "ERROR: newVersion is required (e.g. '1.1.0.0').";

        // Quick semver shape check: 4 numeric parts separated by dots.
        var parts = newVersion.Split('.');
        if (parts.Length != 4 || parts.Any(p => !int.TryParse(p, out _)))
            return $"ERROR: '{newVersion}' is not a valid semver. Use Major.Minor.Build.Revision (e.g. '1.1.0.0').";

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (state.SolutionId == Guid.Empty)
            return "ERROR: No active solution. Select or create a solution first.";

        var oldVersion = string.IsNullOrWhiteSpace(state.SolutionVersion) ? "unknown" : state.SolutionVersion;

        try
        {
            await dataverseClient.UpdateSolutionVersionAsync(state.SolutionId, newVersion, cancellationToken);

            state.SolutionVersion = newVersion;
            state.LastActivityAt = DateTime.UtcNow;

            // Reverse op only makes sense if we knew the old version — undo via re-update.
            var isReversible = !string.Equals(oldVersion, "unknown", StringComparison.Ordinal);
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                ToolName = "UpdateSolutionVersion",
                Parameters = new { solutionId = state.SolutionId, oldVersion, newVersion },
                Result = new { newVersion },
                IsReversible = isReversible,
                ReverseOp = isReversible
                    ? new ReverseOperation
                    {
                        ToolName = "RevertSolutionVersion",
                        Parameters = new { solutionId = state.SolutionId, oldVersion },
                        Warning = "Restores the previous version string. Dataverse rejects downgrades, so this only works if no higher version was set in between.",
                        RiskLevel = RiskLevel.Low
                    }
                    : null
            });
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                Action = "UpdateSolutionVersion",
                Detail = $"Solution '{state.SolutionDisplayName}' version: {oldVersion} → {newVersion}",
                Success = true
            });

            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("Updated solution {SolutionId} version: {Old} → {New}", state.SolutionId, oldVersion, newVersion);

            return $"Solution '{state.SolutionDisplayName}' version updated: {oldVersion} → {newVersion}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UpdateSolutionVersion failed for solution {SolutionId}", state.SolutionId);
            return $"ERROR: Could not update solution version. {ex.Message}";
        }
    }
}
