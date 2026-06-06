using System.ComponentModel;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Creates a new Dataverse publisher. The publisher's customization prefix is what
/// gets prepended to every component logical name (e.g., 'adc' → 'adc_bike').
/// Once a publisher exists it can be selected when creating a solution.
/// </summary>
public class CreatePublisherTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    ILogger<CreatePublisherTool> logger)
{
    [Description("Create a new unmanaged publisher in the Dataverse environment. "
        + "The publisher's customizationPrefix (e.g., 'adc', 'contoso') is prepended to "
        + "every component logical name created in any solution owned by this publisher. "
        + "After creation, pass the returned PublisherId to create_solution. "
        + "Rules: uniqueName is PascalCase no spaces, customizationPrefix is 2-8 lowercase "
        + "letters/digits (NO underscores), customizationOptionValuePrefix is a 5-digit number "
        + "between 10000 and 99999 (must be unique across publishers in the env).")]
    public async Task<string> CreatePublisherAsync(
        [Description("Unique publisher name in PascalCase, no spaces (e.g., 'ContosoCorp')")] string uniqueName,
        [Description("Friendly display name for the publisher (e.g., 'Contoso Corp')")] string friendlyName,
        [Description("Brief description of the publisher")] string description,
        [Description("Customization prefix, 2-8 lowercase letters/digits, no underscores (e.g., 'contoso')")] string customizationPrefix,
        [Description("Customization option value prefix — a 5-digit number 10000-99999, unique across publishers")] int customizationOptionValuePrefix,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customizationPrefix) ||
            customizationPrefix.Length < 2 ||
            customizationPrefix.Length > 8 ||
            customizationPrefix.Any(c => !char.IsLetterOrDigit(c)))
        {
            return $"ERROR: customizationPrefix '{customizationPrefix}' is invalid — must be 2-8 letters/digits, no underscores or special chars.";
        }
        if (customizationOptionValuePrefix < 10000 || customizationOptionValuePrefix > 99999)
        {
            return $"ERROR: customizationOptionValuePrefix {customizationOptionValuePrefix} is out of range — must be 10000-99999.";
        }

        // Idempotency precheck: Dataverse returns HTTP 412 "A record with matching
        // key values already exists" if any publisher with the same uniqueName OR
        // the same customizationPrefix already exists. Past failure: agent saw
        // the 412 but the calling text claimed success, leading to confusion.
        // Detect upfront and return the existing publisher's id so callers get a
        // single happy-path return shape regardless of pre-existence.
        try
        {
            var existing = await dataverseClient.GetPublishersAsync(cancellationToken);
            var prefixLower = customizationPrefix.ToLowerInvariant();
            var match = existing.FirstOrDefault(p =>
                string.Equals(p.UniqueName, uniqueName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Prefix, prefixLower, StringComparison.OrdinalIgnoreCase));
            if (match is not null && match.PublisherId != Guid.Empty)
            {
                logger.LogInformation("CreatePublisher: '{UniqueName}' / prefix '{Prefix}' already exists ({PublisherId}); returning existing",
                    uniqueName, prefixLower, match.PublisherId);
                return $"NOOP: Publisher '{match.DisplayName}' (uniqueName: '{match.UniqueName}', "
                    + $"prefix: '{match.Prefix}_') already exists. PublisherId={match.PublisherId}. "
                    + "Use this PublisherId verbatim when calling create_solution. No create needed.";
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Publisher precheck failed; proceeding with CreatePublisher");
        }

        try
        {
            var publisherId = await dataverseClient.CreatePublisherAsync(
                uniqueName, friendlyName, description, customizationPrefix, customizationOptionValuePrefix, cancellationToken);

            logger.LogInformation("Created publisher {UniqueName} ({PublisherId})", uniqueName, publisherId);

            var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                ToolName = "CreatePublisher",
                Parameters = new { uniqueName, friendlyName, customizationPrefix },
                Result = new { publisherId },
                IsReversible = true,
                ReverseOp = new ReverseOperation
                {
                    ToolName = "DeletePublisher",
                    Parameters = new { publisherId },
                    Warning = "Deleting a publisher only succeeds if no solution still references it. Drop dependent solutions first.",
                    RiskLevel = RiskLevel.High
                }
            });
            state.AuditLog.Add(AuditEntry.Ok(AgentNames.SolutionArchitect, "CreatePublisher", $"Created publisher '{friendlyName}' (prefix '{customizationPrefix}')"));
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            return $"Publisher '{friendlyName}' created with PublisherId={publisherId}, "
                + $"uniqueName='{uniqueName}', prefix='{customizationPrefix.ToLowerInvariant()}_', "
                + $"optionValuePrefix={customizationOptionValuePrefix}. "
                + $"You can now pass publisherId={publisherId} to create_solution.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create publisher {UniqueName}", uniqueName);
            return $"ERROR: Failed to create publisher '{uniqueName}': {ex.Message}";
        }
    }
}
