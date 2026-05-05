using System.Text;
using System.Xml;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Translates a structured <see cref="SiteMapLayoutSpec"/> into the SiteMap
/// XML format that Dataverse expects on the <c>sitemap.sitemapxml</c> column.
/// Validates the spec for shape errors (duplicate Ids per Area/Group,
/// mutual-exclusion of Entity/Url/PageId on a SubArea) BEFORE emitting XML so
/// the agent gets a clean error message instead of a downstream XSD failure.
///
/// The emitted XML is then passed through <see cref="XsdValidator"/> to catch
/// anything the structural rules missed (e.g. invalid attribute values on
/// privileges, malformed icon URLs).
/// </summary>
public static class SiteMapXmlBuilder
{
    /// <summary>
    /// Validate the spec for shape errors. Returns the empty list when valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(SiteMapLayoutSpec spec)
    {
        var errors = new List<string>();

        if (spec is null || spec.Areas is null)
        {
            errors.Add("SiteMap spec is missing — supply at least one Area.");
            return errors;
        }
        if (spec.Areas.Count == 0)
        {
            errors.Add("SiteMap has no Areas — supply at least one Area.");
            return errors;
        }

        // Area Ids must be unique within the SiteMap (XSD enforces this too,
        // but catching it here gives a friendlier error with the duplicated id).
        var areaIds = new HashSet<string>(StringComparer.Ordinal);
        for (var ai = 0; ai < spec.Areas.Count; ai++)
        {
            var area = spec.Areas[ai];
            var areaPath = $"areas[{ai}]";

            if (string.IsNullOrWhiteSpace(area.Id))
            {
                errors.Add($"{areaPath}.id is required (sitemap area id, e.g. 'sales').");
                continue;
            }
            if (!areaIds.Add(area.Id))
            {
                errors.Add($"{areaPath}.id '{area.Id}' is duplicated — each Area's Id must be unique within the SiteMap.");
            }

            // Group Ids must be unique within the AREA (not globally).
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            var groups = area.Groups ?? Array.Empty<SiteMapGroupSpec>();
            for (var gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                var groupPath = $"{areaPath}.groups[{gi}]";

                if (string.IsNullOrWhiteSpace(group.Id))
                {
                    errors.Add($"{groupPath}.id is required.");
                    continue;
                }
                if (!groupIds.Add(group.Id))
                {
                    errors.Add($"{groupPath}.id '{group.Id}' is duplicated within Area '{area.Id}'.");
                }

                // SubArea Ids must be unique within the GROUP.
                var subIds = new HashSet<string>(StringComparer.Ordinal);
                var subs = group.SubAreas ?? Array.Empty<SiteMapSubAreaSpec>();
                for (var si = 0; si < subs.Count; si++)
                {
                    var sub = subs[si];
                    var subPath = $"{groupPath}.subareas[{si}]";

                    if (string.IsNullOrWhiteSpace(sub.Id))
                    {
                        errors.Add($"{subPath}.id is required.");
                        continue;
                    }
                    if (!subIds.Add(sub.Id))
                    {
                        errors.Add($"{subPath}.id '{sub.Id}' is duplicated within Group '{group.Id}'.");
                    }

                    // Mutual exclusion: a SubArea points at exactly ONE of
                    // entity / url / pageId. The XSD allows multiple but the
                    // runtime ignores all but the first; we hard-reject the
                    // ambiguous case so the agent gets a clean error.
                    var pointers = new[]
                    {
                        ("entity", sub.Entity),
                        ("url", sub.Url),
                        ("pageId", sub.PageId),
                    }.Count(p => !string.IsNullOrWhiteSpace(p.Item2));
                    if (pointers > 1)
                    {
                        errors.Add($"{subPath} has more than one of entity / url / pageId set — supply exactly ONE.");
                    }
                }
            }

            // ShowGroups + Groups consistency: if there are Groups, ShowGroups
            // should be true. Otherwise the groups won't render even though
            // they're in the XML — common gotcha.
            if (groups.Count > 0 && !area.ShowGroups)
            {
                errors.Add($"{areaPath} has {groups.Count} group(s) but showGroups=false — set showGroups=true so the groups render.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Emit the SiteMap XML for a validated spec. Caller MUST validate first
    /// via <see cref="Validate"/>; passing an invalid spec produces undefined
    /// output (we don't double-validate to keep this method allocation-light).
    /// </summary>
    public static string Render(SiteMapLayoutSpec spec)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            Indent = true,
            IndentChars = "  ",
        };

        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartElement("SiteMap");

            foreach (var area in spec.Areas)
            {
                writer.WriteStartElement("Area");
                writer.WriteAttributeString("Id", area.Id);
                if (!string.IsNullOrWhiteSpace(area.Title)) writer.WriteAttributeString("Title", area.Title);
                if (!string.IsNullOrWhiteSpace(area.Icon)) writer.WriteAttributeString("Icon", area.Icon);
                writer.WriteAttributeString("ShowGroups", area.ShowGroups ? "true" : "false");

                foreach (var group in area.Groups ?? Array.Empty<SiteMapGroupSpec>())
                {
                    writer.WriteStartElement("Group");
                    writer.WriteAttributeString("Id", group.Id);
                    if (!string.IsNullOrWhiteSpace(group.Title)) writer.WriteAttributeString("Title", group.Title);

                    foreach (var sub in group.SubAreas ?? Array.Empty<SiteMapSubAreaSpec>())
                    {
                        writer.WriteStartElement("SubArea");
                        writer.WriteAttributeString("Id", sub.Id);
                        if (!string.IsNullOrWhiteSpace(sub.Title)) writer.WriteAttributeString("Title", sub.Title);
                        if (!string.IsNullOrWhiteSpace(sub.Icon)) writer.WriteAttributeString("Icon", sub.Icon);

                        // Pointer attributes — exactly one will be non-empty
                        // after Validate.
                        if (!string.IsNullOrWhiteSpace(sub.Entity))
                        {
                            writer.WriteAttributeString("Entity", sub.Entity);
                        }
                        else if (!string.IsNullOrWhiteSpace(sub.Url))
                        {
                            writer.WriteAttributeString("Url", sub.Url);
                        }
                        else if (!string.IsNullOrWhiteSpace(sub.PageId))
                        {
                            // Custom Page references go through Url with a
                            // standard /main.aspx?pageid= shape. Type=PowerPlatform
                            // is also acceptable on newer schemas; we use the
                            // URL form for portability.
                            writer.WriteAttributeString("Url", $"/main.aspx?pagetype=control&controlName=MscrmControls.PowerPlatformControl.PowerPlatformControl&data=%7B%22pageId%22%3A%22{sub.PageId}%22%7D");
                        }

                        if (!string.IsNullOrWhiteSpace(sub.Type)) writer.WriteAttributeString("Type", sub.Type);

                        foreach (var priv in sub.Privileges ?? Array.Empty<SiteMapPrivilegeSpec>())
                        {
                            writer.WriteStartElement("Privilege");
                            if (!string.IsNullOrWhiteSpace(priv.Entity)) writer.WriteAttributeString("Entity", priv.Entity);
                            if (!string.IsNullOrWhiteSpace(priv.Privilege)) writer.WriteAttributeString("Privilege", priv.Privilege);
                            writer.WriteEndElement(); // Privilege
                        }

                        writer.WriteEndElement(); // SubArea
                    }

                    writer.WriteEndElement(); // Group
                }

                writer.WriteEndElement(); // Area
            }

            writer.WriteEndElement(); // SiteMap
        }

        return sb.ToString();
    }
}
