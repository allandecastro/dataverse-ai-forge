using Microsoft.AspNetCore.StaticFiles;

namespace DataverseForge.Api.Endpoints;

/// <summary>
/// Serves files from <c>{AppContext.BaseDirectory}/exports/</c> — the same
/// folder <see cref="DataverseForge.Application.Tools.ExportSolutionTool"/>
/// writes to. Returns the file with <c>Content-Disposition: attachment</c>
/// so browsers initiate a download instead of rendering the zip inline.
///
/// Path-traversal hardened: the route accepts only a SINGLE filename segment
/// (no slashes), and the resolved full path must stay rooted under the
/// exports folder. Anything else returns 404.
/// </summary>
internal static class ExportsEndpoints
{
    public static IEndpointRouteBuilder MapExportsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/exports/{filename}", (string filename, ILogger<Program> logger) =>
        {
            // Reject anything that isn't a plain filename. Defense-in-depth:
            // route constraints already block `/`, but a literal `..` segment
            // could still be passed as a single token; reject those explicitly.
            if (string.IsNullOrWhiteSpace(filename)
                || filename.Contains("..", StringComparison.Ordinal)
                || filename.Contains('/', StringComparison.Ordinal)
                || filename.Contains('\\', StringComparison.Ordinal))
            {
                return Results.NotFound();
            }

            var exportFolder = Path.Combine(AppContext.BaseDirectory, "exports");
            var fullPath = Path.GetFullPath(Path.Combine(exportFolder, filename));

            // Ensure the resolved path stays inside the exports folder. If the
            // OS resolved a symlink/junction outward, this catches it.
            var rootedFolder = Path.GetFullPath(exportFolder)
                + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootedFolder, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Export download rejected (path escape): {FileName}", filename);
                return Results.NotFound();
            }

            if (!File.Exists(fullPath))
            {
                logger.LogInformation("Export download not found: {Path}", fullPath);
                return Results.NotFound();
            }

            // Use the file extension's MIME or fall back to octet-stream so
            // the browser treats it as a download.
            var contentTypeProvider = new FileExtensionContentTypeProvider();
            if (!contentTypeProvider.TryGetContentType(fullPath, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            return Results.File(fullPath, contentType, fileDownloadName: filename);
        });

        return app;
    }
}
