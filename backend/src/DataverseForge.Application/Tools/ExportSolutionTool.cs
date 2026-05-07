using System.ComponentModel;
using System.Diagnostics;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Exports a Dataverse solution as a .zip on the host file system. Wraps the
/// Web API <c>ExportSolution</c> action: the response carries a base64-encoded
/// binary that we decode and writes to a writable runtime data directory
/// (Infrastructure resolves the path; in production it's a mounted volume,
/// in dev it lives next to the binary). Returns a clickable download URL
/// pointing at the backend's <c>/api/exports/{filename}</c> endpoint, plus
/// the disk path as a fallback. The base URL and export-folder provider are
/// supplied at construction so the Application layer stays free of
/// configuration plumbing. Base URL defaults to <c>http://localhost:5000</c>
/// for local dev.
/// </summary>
public class ExportSolutionTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    string publicBaseUrl,
    Func<string> exportPathProvider,
    ILogger<ExportSolutionTool> logger)
{
    [Description("Export a Dataverse solution as a .zip file (unmanaged or managed). Wraps the "
        + "ExportSolution Web API action. The .zip is saved to the backend's local 'exports' folder; "
        + "the tool returns a clickable download URL the user can click to download the zip. "
        + "PUBLISHES the environment FIRST by default so any unpublished schema/UI changes are "
        + "included in the export — pass publishFirst=false to skip the publish (faster, but you "
        + "MUST be sure everything is already published or the export will be missing changes). "
        + "Use this when the user wants to download/ship/back-up their solution. "
        + "Call request_approval first — exports + publishes take time on large solutions and "
        + "produce real, visible artifacts.")]
    public async Task<string> ExportSolutionAsync(
        [Description("Unique name of the solution to export (NOT display name; e.g. 'eppcGiftSolution', 'cr123_workorder')")] string solutionUniqueName,
        [Description("'managed' to export as a managed solution (locks editing for downstream consumers), 'unmanaged' for a development/transferable copy. Default 'unmanaged' for development.")] string managedOrUnmanaged,
        [Description("True (default, recommended) to call PublishAllXml BEFORE export so unpublished changes are included. False only when you're certain the env is already published — saves the publish round-trip.")] bool publishFirst,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(solutionUniqueName))
            return "ERROR: solutionUniqueName is required.";

        var managed = string.Equals(managedOrUnmanaged, "managed", StringComparison.OrdinalIgnoreCase);
        logger.LogInformation("ExportSolution executing: solution={Solution}, managed={Managed}", solutionUniqueName, managed);

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        try
        {
            // Publish first so the export includes unpublished schema/UI changes.
            // Best-effort within the export flow: a publish failure shouldn't
            // block the export (the env may already be published, or the user
            // may have explicitly opted out). Surface the warning in the result.
            var publishNote = string.Empty;
            if (publishFirst)
            {
                try
                {
                    await dataverseClient.PublishAllAsync(cancellationToken);
                    publishNote = "Published all customizations before export.";
                }
                catch (Exception pubEx) when (pubEx is not OperationCanceledException)
                {
                    logger.LogWarning(pubEx, "PublishAllXml failed before export of '{Solution}' — proceeding without publish",
                        solutionUniqueName);
                    publishNote = $"WARNING: PublishAllXml failed ({pubEx.Message}); export may miss "
                        + "unpublished changes.";
                }
            }
            else
            {
                publishNote = "Skipped publish (publishFirst=false). Export reflects last-published state only.";
            }

            var bytes = await dataverseClient.ExportSolutionAsync(solutionUniqueName, managed, cancellationToken);
            if (bytes is null || bytes.Length == 0)
            {
                return $"ERROR: ExportSolution returned an empty payload for '{solutionUniqueName}'. "
                    + "The solution may not exist, or the user account doesn't have permission to export.";
            }

            // Persist to <DataDir>/exports/<solution>_<managed|unmanaged>_<timestamp>.zip.
            // Filename includes timestamp + flavour so consecutive exports don't collide.
            // Goes through ForgePaths so the operator can mount a real volume via the
            // DVFORGE_DATA_DIR env var; in dev it falls back to the binary's data/ dir.
            // F-43.
            var exportFolder = exportPathProvider();
            var flavour = managed ? "managed" : "unmanaged";
            var fileName = $"{solutionUniqueName}_{flavour}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip";
            var fullPath = Path.Combine(exportFolder, fileName);
            await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);

            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                Action = "ExportSolution",
                Detail = $"Exported '{solutionUniqueName}' ({flavour}, {bytes.Length:N0} bytes) → {fullPath}",
                Success = true
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExportSolution"), new KeyValuePair<string, object?>("tool.status", "success"));

            // Surface a markdown download link pointing straight at the
            // backend's /api/exports/{filename} endpoint. Clicking the link is
            // a top-level navigation (not a fetch), so CORS is irrelevant; the
            // backend serves with Content-Disposition: attachment which
            // triggers the browser's download flow.
            // The base URL is configurable for non-localhost deployments via
            // Forge:PublicBaseUrl. Default = http://localhost:5000 (matches
            // the API's launchSettings).
            var sizeMb = bytes.Length / 1024.0 / 1024.0;
            var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
                ? "http://localhost:5000"
                : publicBaseUrl.TrimEnd('/');
            var downloadUrl = $"{baseUrl}/api/exports/{Uri.EscapeDataString(fileName)}";
            return $"{publishNote}\n\n"
                + $"Exported solution '{solutionUniqueName}' as {flavour} ({sizeMb:F2} MB).\n\n"
                + $"**[Download {fileName}]({downloadUrl})**\n\n"
                + $"_Server path:_ `{fullPath}`";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ExportSolution failed: solution={Solution}", solutionUniqueName);
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = AgentNames.SolutionArchitect,
                Action = "ExportSolution",
                Detail = $"Failed to export '{solutionUniqueName}': {ex.Message}",
                Success = false
            });
            await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            sw.Stop();
            ForgeActivitySource.ToolExecutionDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("tool.name", "ExportSolution"), new KeyValuePair<string, object?>("tool.status", "error"));
            return $"ERROR: Could not export solution '{solutionUniqueName}': {ex.Message}. "
                + "Common causes: the solution doesn't exist (check the UNIQUE name, not the display name), "
                + "the user account lacks Export permission, or the solution contains unmanaged dependencies "
                + "blocking a managed export.";
        }
    }
}
