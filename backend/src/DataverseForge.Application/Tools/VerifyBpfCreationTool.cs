using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyBpfCreationTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created Business Process Flow exists in Dataverse and matches the expected specification. "
        + "Re-reads the BPF from the Dataverse API and compares properties. "
        + "Returns PASS or FAIL verdict with any discrepancies found.")]
    public async Task<VerificationResult> VerifyBpfCreationAsync(
        [Description("The BPF name to verify")] string bpfName,
        [Description("Expected primary table logical name")] string expectedPrimaryTable,
        CancellationToken cancellationToken)
    {
        BpfMetadata actual;
        try
        {
            actual = await dataverseClient.GetBpfMetadataAsync(bpfName, cancellationToken);
        }
        catch (Exception)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("bpf", bpfName, "NOT FOUND")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        if (!string.Equals(actual.PrimaryTable, expectedPrimaryTable, StringComparison.OrdinalIgnoreCase))
            discrepancies.Add(new Discrepancy("PrimaryTable", expectedPrimaryTable, actual.PrimaryTable));

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";
        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: verdict);
    }
}
