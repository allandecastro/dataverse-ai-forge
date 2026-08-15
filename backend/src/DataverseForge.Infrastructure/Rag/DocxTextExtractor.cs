using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Extracts plain text from a .docx file using DocumentFormat.OpenXml.
/// Preserves paragraph boundaries as newlines; ignores headers/footers/footnotes
/// to keep output clean for embedding.
/// </summary>
public static class DocxTextExtractor
{
    public static string Extract(string filePath)
    {
        using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();

        foreach (var para in body.Descendants<Paragraph>())
        {
            var text = string.Concat(para.Descendants<Text>().Select(t => t.Text));
            if (!string.IsNullOrWhiteSpace(text))
                sb.AppendLine(text);
        }

        return sb.ToString();
    }
}
