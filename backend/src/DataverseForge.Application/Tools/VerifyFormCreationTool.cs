using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class VerifyFormCreationTool(IDataverseClient dataverseClient)
{
    [Description("Verify that a created form exists on the target table and matches the expected specification. "
        + "Re-reads the form from the Dataverse API and compares name and type. "
        + "Returns PASS, FAIL, or PARTIAL verdict with any discrepancies found.")]
    public async Task<VerificationResult> VerifyFormCreationAsync(
        [Description("The logical name of the table the form belongs to")] string tableLogicalName,
        [Description("The GUID of the form to verify")] string formId,
        [Description("Expected form name")] string expectedFormName,
        [Description("Expected form type (main, quickCreate, quickView)")] string expectedFormType,
        CancellationToken cancellationToken)
    {
        FormDefinition actual;
        try
        {
            actual = await dataverseClient.GetFormDefinitionAsync(Guid.Parse(formId), cancellationToken);
        }
        catch (Exception)
        {
            return new VerificationResult(
                Exists: false,
                Matches: false,
                Discrepancies: [new Discrepancy("form", formId, "NOT FOUND")],
                Verdict: "FAIL");
        }

        var discrepancies = new List<Discrepancy>();

        CompareProperty(discrepancies, "Name", expectedFormName, actual.Name);
        CompareProperty(discrepancies, "FormType", expectedFormType, actual.FormType);

        var verdict = discrepancies.Count == 0 ? "PASS" : "FAIL";
        return new VerificationResult(
            Exists: true,
            Matches: discrepancies.Count == 0,
            Discrepancies: discrepancies,
            Verdict: verdict);
    }

    private static void CompareProperty(List<Discrepancy> discrepancies, string property, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            discrepancies.Add(new Discrepancy(property, expected, actual));
        }
    }
}
