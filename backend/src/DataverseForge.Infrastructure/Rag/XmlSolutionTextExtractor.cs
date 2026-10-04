using System.Xml.Linq;

namespace DataverseForge.Infrastructure.Rag;

/// <summary>
/// Extracts human-readable text from Dataverse solution XML files (customizations.xml,
/// formxml, savedquery, etc.) using XPath-aware traversal.
///
/// Strategy: extract display names, descriptions, and label values from well-known
/// Dataverse XML element paths. Falls back to stripping all XML tags for unrecognised
/// files so no content is lost.
/// </summary>
public static class XmlSolutionTextExtractor
{
    public static string Extract(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return string.Empty;

        try
        {
            var doc = XDocument.Parse(xml);
            var sb = new System.Text.StringBuilder();

            // Dataverse display names and descriptions live inside <label> elements
            // with a languagecode attribute, or as DisplayName/Description attributes.
            foreach (var el in doc.Descendants())
            {
                // DisplayName / Description attributes (EntityMetadata, AttributeMetadata, etc.)
                foreach (var attrName in new[] { "DisplayName", "Description", "LocalizedName", "LocalizedDescription" })
                {
                    var attr = el.Attribute(attrName);
                    if (attr is not null && !string.IsNullOrWhiteSpace(attr.Value))
                        sb.AppendLine(attr.Value);
                }

                // <label description="..." languagecode="1036" /> elements
                if (el.Name.LocalName == "label")
                {
                    var desc = el.Attribute("description");
                    if (desc is not null && !string.IsNullOrWhiteSpace(desc.Value))
                        sb.AppendLine(desc.Value);
                }

                // FetchXML query descriptions (savedquery / userquery)
                if (el.Name.LocalName is "savedquery" or "userquery")
                {
                    var name = el.Element("name") ?? el.Attribute("name") as object;
                    var text = (name as XElement)?.Value ?? (name as XAttribute)?.Value;
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                }
            }

            var result = sb.ToString().Trim();

            // Fallback: if structured extraction found nothing, strip all tags
            if (string.IsNullOrWhiteSpace(result))
            {
                result = System.Text.RegularExpressions.Regex.Replace(xml, "<[^>]+>", " ");
                result = System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ").Trim();
            }

            return result;
        }
        catch
        {
            // Not valid XML — return as-is (best effort)
            return xml;
        }
    }
}
