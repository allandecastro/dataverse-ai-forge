using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifySecurityRoleTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created security role exists in Dataverse and matches the expected specification. "
        + "Re-reads the role from the API and compares properties. "
        + "Returns PASS or FAIL verdict with any discrepancies found.")]
    public async Task<VerificationResult> VerifySecurityRoleAsync(
        [Description("The GUID of the security role to verify")] Guid roleId,
        [Description("The expected role name")] string expectedRoleName,
        CancellationToken cancellationToken)
    {
        SecurityRoleDetails actual;
        try
        {
            actual = await dataverseClient.GetSecurityRoleDetailsAsync(roleId, cancellationToken);
        }
        catch (Exception)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("role", expectedRoleName, "NOT FOUND")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        if (!string.Equals(actual.Name, expectedRoleName, StringComparison.OrdinalIgnoreCase))
            discrepancies.Add(new Discrepancy("Name", expectedRoleName, actual.Name));

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";
        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: verdict);
    }
}
