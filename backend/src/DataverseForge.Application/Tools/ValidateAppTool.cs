using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ValidateAppTool(IDataverseClient dataverseClient)
{
    [Description("Validate a model-driven app via the unbound ValidateApp action. Returns IsValid + "
        + "the list of validation issues (severity / message / componentName / componentId / "
        + "componentType). Read-only. Use this after create / update / sitemap edits to confirm "
        + "the app can be opened by users.")]
    public async Task<AppValidationResult> ValidateAppAsync(
        [Description("appmoduleid (GUID) — get from get_apps_list.")] Guid appId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await dataverseClient.ValidateAppAsync(appId, cancellationToken);
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ValidateApp", result.IsValid ? "success" : "invalid", sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Surface the actual Dataverse error as a structured "Error" issue
            // instead of letting the function-call layer collapse it into an
            // opaque "Function failed." The agent can quote the message back
            // to the user verbatim.
            sw.Stop();
            ForgeActivitySource.RecordToolDuration("ValidateApp", "error", sw.ElapsedMilliseconds);
            return new AppValidationResult(
                appId,
                IsValid: false,
                Issues: new List<AppValidationIssue>
                {
                    new("Error", $"ValidateApp call failed: {ex.Message}", null, null, null),
                }.AsReadOnly());
        }
    }
}
