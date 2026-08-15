using System.Text.RegularExpressions;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Strips timing cues and metadata from WebVTT (.vtt) transcript files,
/// returning only the spoken text — suitable for embedding.
///
/// Example VTT block:
/// <code>
/// 00:01.000 --> 00:04.000
/// Alice: Bonjour tout le monde.
/// </code>
/// </summary>
public static partial class VttTextExtractor
{
    [GeneratedRegex(@"^\d{2}:\d{2}[.:]\d{3}\s*-->\s*\d{2}:\d{2}[.:]\d{3}.*$", RegexOptions.Multiline)]
    private static partial Regex TimingLine();

    [GeneratedRegex(@"^WEBVTT.*$", RegexOptions.Multiline)]
    private static partial Regex HeaderLine();

    public static string Extract(string vttContent)
    {
        if (string.IsNullOrWhiteSpace(vttContent))
            return string.Empty;

        // Remove WEBVTT header and timing lines
        var stripped = TimingLine().Replace(vttContent, string.Empty);
        stripped = HeaderLine().Replace(stripped, string.Empty);

        // Collapse multiple blank lines
        stripped = Regex.Replace(stripped, @"\n{3,}", "\n\n");

        return stripped.Trim();
    }
}
