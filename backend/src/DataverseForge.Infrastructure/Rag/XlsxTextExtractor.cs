using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Extracts plain text from Excel (.xlsx) files using DocumentFormat.OpenXml.
/// Each worksheet is rendered as tab-separated rows. Cell values are resolved
/// including shared strings and formula cached values.
/// </summary>
public static class XlsxTextExtractor
{
    public static string Extract(string filePath)
    {
        using var doc = SpreadsheetDocument.Open(filePath, isEditable: false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart is null)
            return string.Empty;

        // Build shared-string lookup (most xlsx files use shared strings for text cells)
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
            .Elements<SharedStringItem>()
            .Select(x => x.InnerText)
            .ToArray() ?? [];

        var sb = new System.Text.StringBuilder();

        foreach (var sheet in workbookPart.Workbook.Sheets?.OfType<Sheet>() ?? [])
        {
            var relId = sheet.Id?.Value;
            if (relId is null) continue;

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relId);
            var sheetName = sheet.Name?.Value ?? "Sheet";

            sb.AppendLine($"[Sheet: {sheetName}]");

            foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
            {
                var cells = row.Elements<Cell>()
                    .Select(c => GetCellValue(c, sharedStrings))
                    .Where(v => !string.IsNullOrWhiteSpace(v));

                var rowText = string.Join("\t", cells);
                if (!string.IsNullOrWhiteSpace(rowText))
                    sb.AppendLine(rowText);
            }

            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    private static string GetCellValue(Cell cell, string[] sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? string.Empty;

        // Shared string: the value is an index into the shared-string table
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out var idx)
            && idx < sharedStrings.Length)
        {
            return sharedStrings[idx];
        }

        return value;
    }
}
