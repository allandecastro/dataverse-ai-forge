using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class GetPrivilegesForTableTool(IDataverseClient dataverseClient)
{
    [Description("List the Dataverse privileges defined on a table — the eight access rights "
        + "(Create / Read / Write / Delete / Append / AppendTo / Assign / Share) along with "
        + "the four CanBe* depth flags (CanBeBasic / CanBeLocal / CanBeDeep / CanBeGlobal). "
        + "Call this ONCE PER DISTINCT TABLE before plan_create_security_role / "
        + "plan_update_security_role so you have real PrivilegeIds to pass — never invent a "
        + "privilege GUID. Empty result means the table doesn't exist; double-check the "
        + "logical name (Dataverse logical names are lowercase).")]
    public async Task<IReadOnlyList<PrivilegeMetadata>> GetPrivilegesForTableAsync(
        [Description("Lowercase logical name of the target table (e.g. 'account', 'adc_feedback')")]
        string tableLogicalName,
        CancellationToken cancellationToken = default)
    {
        return await dataverseClient.GetPrivilegesForTableAsync(tableLogicalName, cancellationToken);
    }
}
