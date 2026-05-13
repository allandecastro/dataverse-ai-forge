using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

public class GetPrivilegesForTableTool(
    IDataverseClient dataverseClient,
    ILogger<GetPrivilegesForTableTool> logger)
{
    [Description("List the Dataverse privileges defined on a table — the eight access rights "
        + "(Create / Read / Write / Delete / Append / AppendTo / Assign / Share) along with "
        + "the four CanBe* depth flags (CanBeBasic / CanBeLocal / CanBeDeep / CanBeGlobal). "
        + "Returns a result object with Success/ErrorMessage/Privileges — on success "
        + "Privileges holds the list; on failure ErrorMessage carries the verbatim Dataverse "
        + "error so you can quote it and pick the right next step. Call this ONCE PER DISTINCT "
        + "TABLE before plan_create_security_role / plan_update_security_role so you have real "
        + "PrivilegeIds to pass — never invent a privilege GUID.")]
    public async Task<PrivilegesForTableResult> GetPrivilegesForTableAsync(
        [Description("Lowercase logical name of the target table (e.g. 'account', 'adc_feedback')")]
        string tableLogicalName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var list = await dataverseClient.GetPrivilegesForTableAsync(tableLogicalName, cancellationToken);
            return new PrivilegesForTableResult(
                Success: true,
                ErrorMessage: null,
                TableLogicalName: tableLogicalName,
                Privileges: list);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surface the real error to the agent. Without this, the AIFunctionFactory
            // wrapper converts the HttpRequestException into a generic "Function failed."
            // string and the agent loses the Dataverse OData error body.
            logger.LogWarning(ex, "GetPrivilegesForTable failed for {TableLogicalName}", tableLogicalName);
            return new PrivilegesForTableResult(
                Success: false,
                ErrorMessage: ex.Message,
                TableLogicalName: tableLogicalName,
                Privileges: Array.Empty<PrivilegeMetadata>());
        }
    }
}
