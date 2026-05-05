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
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<SelectSolutionTool> logger)
{
    [Description("Mark an EXISTING Dataverse solution as the active solution for this session. "
        + "Call this AFTER the user picks an existing solution from get_solutions_list (do NOT call "
        + "for new solutions — use create_solution instead). The tool looks up the real solution by "
        + "uniqueName, so even if you mistype a GUID it will recover automatically as long as "
        + "uniqueName matches the get_solutions_list result.")]
    public async Task<string> SelectSolutionAsync(
        [Description("Solution GUID from get_solutions_list (SolutionId field) — copy the WHOLE GUID character-for-character. If unsure, the tool will recover via uniqueName.")] string solutionId,
        [Description("Solution unique name (UniqueName field) — REQUIRED, used as the canonical lookup key")] string uniqueName,
        [Description("Solution display/friendly name (DisplayName field)")] string displayName,
        [Description("Solution version (Version field)")] string version,
        [Description("Publisher GUID (PublisherId field) — copy the WHOLE GUID character-for-character")] string publisherId,
        [Description("Publisher display name (PublisherName field)")] string publisherDisplayName,
        [Description("Publisher customization prefix (PublisherPrefix field) — e.g. 'adc'")] string publisherPrefix,
        CancellationToken cancellationToken)
    {
        // String parameters (not Guid) on purpose: the framework's argument
        // deserializer fails BEFORE entering the function when an LLM passes a
        // malformed/truncated GUID, returning a generic "Error: Function failed."
        // that gives the agent no actionable signal. Accepting strings lets us:
        //   1) parse-and-recover for common typos / hallucinated GUIDs,
        //   2) return a clear, retryable error otherwise.
        if (string.IsNullOrWhiteSpace(uniqueName))
        {
            return "ERROR: uniqueName is required. Pass the UniqueName field from get_solutions_list.";
        }

        IReadOnlyList<DataverseForge.Domain.Models.SolutionInfo>? allSolutions = null;
        try
        {
            allSolutions = await dataverseClient.GetSolutionsListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SelectSolution validation lookup failed; will accept agent-provided values as-is");
        }

        // Resolve the real solution by GUID first, then by uniqueName as fallback.
        // This is the recovery path for the common LLM bug: solutionId truncated by
        // a tokenization boundary (e.g. last 4 chars cut off).
        var resolvedSolutionId = TryParseGuid(solutionId);
        var resolvedPublisherId = TryParseGuid(publisherId);

        if (allSolutions is not null)
        {
            DataverseForge.Domain.Models.SolutionInfo? match = null;

            // Resolve in priority order. Each step returns NULL if it can't
            // find a UNIQUE match — we don't auto-pick when there's ambiguity.
            //   1. solutionId (real GUID, not Guid.Empty)
            if (resolvedSolutionId is { } sid && sid != Guid.Empty)
            {
                match = allSolutions.FirstOrDefault(s => s.SolutionId == sid);
            }
            //   2. exact uniqueName (case-insensitive)
            match ??= allSolutions.FirstOrDefault(s =>
                string.Equals(s.UniqueName, uniqueName, StringComparison.OrdinalIgnoreCase));
            //   3. exact displayName (case-insensitive) — agent commonly passes the
            //      friendly name in `uniqueName` because the prompt examples lead with display.
            match ??= allSolutions.FirstOrDefault(s =>
                string.Equals(s.DisplayName, uniqueName, StringComparison.OrdinalIgnoreCase));
            //   4. publisher prefix exact match — agent passed "eppc" expecting to
            //      reach the only solution under that publisher. If MORE than one
            //      solution shares the prefix, this is ambiguous; surface the list.
            if (match is null && !string.IsNullOrWhiteSpace(uniqueName))
            {
                var prefixCandidates = allSolutions
                    .Where(s => string.Equals(s.PublisherPrefix, uniqueName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (prefixCandidates.Count == 1) match = prefixCandidates[0];
                else if (prefixCandidates.Count > 1)
                {
                    var names = string.Join(", ", prefixCandidates.Select(c => c.DisplayName));
                    return $"ERROR: '{uniqueName}' looks like a publisher prefix shared by {prefixCandidates.Count} solutions ({names}). "
                        + "Pass the exact UniqueName (or DisplayName) of the one you want.";
                }
            }
            //   5. partial match on uniqueName / displayName — last-resort recovery
            //      for the common "use EPPC" -> "EPPCLiveDemo" case. Only auto-select
            //      when EXACTLY one solution matches; otherwise list candidates.
            if (match is null && !string.IsNullOrWhiteSpace(uniqueName))
            {
                var partialCandidates = allSolutions
                    .Where(s =>
                        s.UniqueName.Contains(uniqueName, StringComparison.OrdinalIgnoreCase)
                        || s.DisplayName.Contains(uniqueName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (partialCandidates.Count == 1) match = partialCandidates[0];
                else if (partialCandidates.Count > 1)
                {
                    var names = string.Join(", ", partialCandidates.Take(5).Select(c => $"'{c.DisplayName}' (uniqueName={c.UniqueName})"));
                    return $"ERROR: '{uniqueName}' matches {partialCandidates.Count} solutions: {names}. "
                        + "Pass the exact UniqueName from get_solutions_list to disambiguate.";
                }
            }

            if (match is null)
            {
                return $"ERROR: Solution '{uniqueName}' (id '{solutionId}') not found in this environment. "
                    + "Call get_solutions_list and copy the UniqueName + SolutionId from the result.";
            }

            if (match.SolutionId != resolvedSolutionId)
            {
                logger.LogWarning("SelectSolution: agent passed solutionId='{Bad}' for uniqueName='{Name}', recovered to {Real}",
                    solutionId, uniqueName, match.SolutionId);
            }

            // Use the AUTHORITATIVE values from Dataverse, not whatever the agent passed.
            resolvedSolutionId = match.SolutionId;
            resolvedPublisherId = match.PublisherId;
            publisherPrefix = match.PublisherPrefix;
            publisherDisplayName = match.PublisherName;
            uniqueName = match.UniqueName;
            displayName = match.DisplayName;
            version = match.Version;
        }
        else
        {
            // No solutions list (network blip): require valid GUIDs from the agent.
            if (resolvedSolutionId is null)
                return $"ERROR: solutionId '{solutionId}' is not a valid GUID and the solutions list lookup failed. Retry after calling get_solutions_list.";
            if (resolvedPublisherId is null)
                return $"ERROR: publisherId '{publisherId}' is not a valid GUID and the solutions list lookup failed. Retry after calling get_solutions_list.";
        }

        try
        {
            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.SolutionId = resolvedSolutionId!.Value;
            state.SolutionUniqueName = uniqueName;
            state.SolutionDisplayName = displayName;
            state.SolutionVersion = version;
            state.PublisherId = resolvedPublisherId ?? Guid.Empty;
            state.PublisherDisplayName = publisherDisplayName;
            state.PublisherPrefix = publisherPrefix;
            state.SolutionConfirmed = true;
            state.LastActivityAt = DateTime.UtcNow;
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            logger.LogInformation("Selected existing solution {Solution} ({SolutionId}) for session {SessionId}",
                uniqueName, state.SolutionId, sessionContext.SessionId);

            return $"Active solution set to '{displayName}' (uniqueName: {uniqueName}, prefix: {publisherPrefix}_, "
                + $"version: {version}). All subsequent components will be added to this solution.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to select solution {UniqueName}", uniqueName);
            return $"ERROR: Failed to select solution: {ex.Message}";
        }
    }

    private static Guid? TryParseGuid(string? value) =>
        Guid.TryParse(value, out var g) ? g : null;
}
