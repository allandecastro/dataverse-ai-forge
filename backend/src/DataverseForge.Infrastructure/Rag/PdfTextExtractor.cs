using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Extracts plain text from a .pdf file using PdfPig.
/// Each page is separated by a newline; word boundaries are inferred from
/// the letter positions returned by PdfPig's layout engine.
/// </summary>
public static class PdfTextExtractor
{
    public static string Extract(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var sb = new System.Text.StringBuilder();

        foreach (var page in document.GetPages())
        {
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text);
        }

        return sb.ToString();
    }
}
