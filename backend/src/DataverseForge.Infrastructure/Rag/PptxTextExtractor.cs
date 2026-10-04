using DocumentFormat.OpenXml.Packaging;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Extracts plain text from PowerPoint (.pptx) files using DocumentFormat.OpenXml.
/// Each slide is rendered as a paragraph of space-separated text runs.
/// Notes are included after the slide content.
/// </summary>
public static class PptxTextExtractor
{
    public static string Extract(string filePath)
    {
        using var doc = PresentationDocument.Open(filePath, isEditable: false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart is null)
            return string.Empty;

        var sb = new System.Text.StringBuilder();

        var slideIds = presentationPart.Presentation.SlideIdList?.ChildElements;
        if (slideIds is null)
            return string.Empty;

        var slideIndex = 0;
        foreach (var slideId in slideIds)
        {
            slideIndex++;
            var relId = ((DocumentFormat.OpenXml.Presentation.SlideId)slideId).RelationshipId?.Value;
            if (relId is null) continue;

            var slidePart = (SlidePart)presentationPart.GetPartById(relId);

            // Extract all text runs from the slide
            var slideTexts = slidePart.Slide
                .Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t));

            var slideContent = string.Join(" ", slideTexts).Trim();
            if (!string.IsNullOrWhiteSpace(slideContent))
                sb.AppendLine($"[Slide {slideIndex}] {slideContent}");

            // Include speaker notes if present
            if (slidePart.NotesSlidePart is { } notesPart)
            {
                var noteTexts = notesPart.NotesSlide
                    .Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                var notes = string.Join(" ", noteTexts).Trim();
                if (!string.IsNullOrWhiteSpace(notes))
                    sb.AppendLine($"  [Notes] {notes}");
            }
        }

        return sb.ToString().Trim();
    }
}
