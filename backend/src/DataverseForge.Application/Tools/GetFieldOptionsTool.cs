using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Lists the existing options (value, label, color, optional state mapping) on
/// a Dataverse picklist / multi-select picklist / statuscode field. Works for
/// BOTH local option sets (table-attached) and global option sets — the caller
/// flags which kind via <c>isGlobal</c>. Read-only — no approval needed.
/// </summary>
public class GetFieldOptionsTool(
    IDataverseClient dataverseClient,
    ILogger<GetFieldOptionsTool> logger)
{
    [Description("List existing options on a Dataverse picklist / multi-select picklist / statuscode "
        + "field. Returns each option's Value, Label, Color (hex like '#FF0000' or null), and "
        + "StateValue (only populated for statuscode — maps the reason to Active=0/Inactive=1). "
        + "Read-only. Use this BEFORE plan_update_option / plan_add_option_to_option_set so you "
        + "know the existing values + colors and can pass the correct integer Value to update. "
        + "Works for both local picklists (table-attached, e.g. eppc_gift.eppc_tags) and "
        + "global option sets — set isGlobal accordingly.")]
    public async Task<IReadOnlyList<OptionInfo>> GetFieldOptionsAsync(
        [Description("True for global option set, false for table-local picklist / multi-select / statuscode")] bool isGlobal,
        [Description("Global option set name (used only when isGlobal=true; e.g. 'eppc_giftcategory')")] string optionSetName,
        [Description("Table logical name (used only when isGlobal=false; e.g. 'eppc_gift')")] string tableLogicalName,
        [Description("Field logical name (used only when isGlobal=false; e.g. 'eppc_tags' or 'statuscode')")] string fieldLogicalName,
        CancellationToken cancellationToken)
    {
        if (isGlobal)
        {
            logger.LogInformation("GetFieldOptions: global option set '{Name}'", optionSetName);
            return await dataverseClient.GetGlobalOptionSetOptionsAsync(optionSetName, cancellationToken);
        }
        logger.LogInformation("GetFieldOptions: local on {Table}.{Field}", tableLogicalName, fieldLogicalName);
        return await dataverseClient.GetLocalOptionSetOptionsAsync(tableLogicalName, fieldLogicalName, cancellationToken);
    }
}
