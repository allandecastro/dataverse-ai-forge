using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyViewCreationTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created view exists on the target table and matches the expected specification. "
        + "Re-reads the views from the Dataverse API and checks that a view with the expected name exists. "
        + "Returns PASS or FAIL verdict with any discrepancies found.")]
    public async Task<VerificationResult> VerifyViewCreationAsync(
        [Description("The logical name of the table the view belongs to")] string tableLogicalName,
        [Description("Expected view name")] string expectedViewName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ViewInfo> views;
        try
        {
            views = await dataverseClient.GetViewsListAsync(tableLogicalName, cancellationToken);
        }
        catch (Exception)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("view", expectedViewName, "TABLE NOT FOUND OR INACCESSIBLE")],
                Verdict: "FAIL");
        }

        var matchingView = views.FirstOrDefault(v =>
            string.Equals(v.Name, expectedViewName, StringComparison.OrdinalIgnoreCase));

        if (matchingView is null)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("ViewName", expectedViewName, "NOT FOUND")],
                Verdict: "FAIL");
        }

        return new VerificationResult(
            Exists: true,
            Matches: true,
            Discrepancies: [],
            Verdict: "PASS");
    }
}
