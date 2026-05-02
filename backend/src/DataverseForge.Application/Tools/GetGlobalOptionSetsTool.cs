using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Lists existing global option sets (with their options) so the Data Model Builder
/// can decide whether to reuse one rather than creating a duplicate. Critical for the
/// "always create global Choices" flow — without this, every Choice request spawns a
/// new global option set even when an identical one already exists.
/// </summary>
public class GetGlobalOptionSetsTool(IDataverseClient dataverseClient)
{
    [Description("List existing global option sets in the environment. Returns each one's "
        + "Name, DisplayName, IsCustom, IsManaged and the full Options array (Value + Label). "
        + "Optional searchTerm matches against name, displayName, or option labels — use it to "
        + "find candidates before creating a new global option set (e.g. searchTerm='priority' "
        + "before creating 'adi_priority'). Set customOnly=true to filter out OOB option sets. "
        + "Call this BEFORE plan_create_global_option_set so the user can reuse an existing one.")]
    public async Task<IReadOnlyList<GlobalOptionSetSummary>> GetGlobalOptionSetsAsync(
        [Description("Optional substring to match against name / displayName / option labels (case-insensitive). Empty = list all.")] string searchTerm,
        [Description("If true, only return custom (publisher-prefixed) option sets, not OOB ones.")] bool customOnly,
        CancellationToken cancellationToken)
    {
        return await dataverseClient.GetGlobalOptionSetsAsync(
            string.IsNullOrWhiteSpace(searchTerm) ? null : searchTerm,
            customOnly,
            cancellationToken);
    }
}
