using System.Text;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// FormXML compilers extracted from PlanCreateFormTool (SRP; mirrors the
/// DashboardXmlBuilder / SiteMapXmlBuilder convention). Shared by
/// PlanCreateFormTool and PlanUpdateFormTool — both need identical XML, only the
/// HTTP verb differs. Pure string generation, no Dataverse I/O.
/// </summary>
internal static class FormXmlBuilder
{
    /// <summary>
    /// Public re-export of the FormXML builder so PlanUpdateFormTool can reuse
    /// the exact same XML generation. Both create and update need identical XML
    /// — only the HTTP verb differs.
    /// </summary>
    internal static string BuildFormXmlPublic(
        string tableLogicalName,
        string formName,
        string formType,
        List<string> tab1Fields,
        List<string> tab2Fields,
        List<string> tab3Subgrids,
        bool includeTimeline,
        List<string> headerFields,
        IDictionary<string, string>? fieldDisplayNames = null)
        => BuildFormXml(tableLogicalName, formName, formType, tab1Fields,
            tab2Fields, tab3Subgrids, includeTimeline, headerFields,
            fieldDisplayNames ?? new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>
    /// Builds Main / QuickCreate / QuickView FormXML matching the structure that
    /// Dataverse itself emits (per a real systemforms PATCH payload):
    ///   - <c>&lt;header&gt;</c> / <c>&lt;footer&gt;</c> are SIBLINGS of <c>&lt;tabs&gt;</c>, AFTER it
    ///   - <c>IsUserDefined="1"</c> on tab, <c>IsUserDefined="0"</c> + <c>showlabel="false"</c>
    ///     + <c>showbar="false"</c> on section
    ///   - Every cell carries a <c>&lt;labels&gt;&lt;label description="..." /&gt;&lt;/labels&gt;</c>
    ///     element with the field's display name (so the maker portal renders the
    ///     correct caption — without this, fields show as their logical name)
    ///   - Header uses <c>celllabelposition="Top" columns="111" labelwidth="115"
    ///     celllabelalignment="Left"</c> (the actual attribute set Dataverse uses)
    ///   - Notes/Timeline tab uses classid <c>{06375649-...}</c> (the real Notes
    ///     control), not the legacy <c>{B603CF9F-...}</c>
    /// </summary>
    internal static string BuildFormXml(
        string tableLogicalName,
        string formName,
        string formType,
        List<string> tab1Fields,
        List<string> tab2Fields,
        List<string> tab3Subgrids,
        bool includeTimeline,
        List<string> headerFields,
        IDictionary<string, string> fieldDisplayNames)
    {
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<form headerdensity=\"HighWithControls\">");

        xml.AppendLine("  <tabs>");

        // Tab 1 - General/Summary
        if (tab1Fields.Count > 0)
        {
            var tabId = NewGuid();
            var sectionId = NewGuid();
            // labelid is FormGuidType — must be a GUID if present. We use the
            // <labels> child element instead and omit labelid entirely.
            xml.AppendLine($"    <tab verticallayout=\"true\" id=\"{tabId}\" IsUserDefined=\"1\">");
            xml.AppendLine($"      <labels><label description=\"General\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine($"          <section showlabel=\"false\" showbar=\"false\" IsUserDefined=\"0\" id=\"{sectionId}\">");
            xml.AppendLine($"            <labels><label description=\"General\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows>");
            foreach (var field in tab1Fields)
            {
                AppendFieldCell(xml, field, fieldDisplayNames, indent: "              ");
            }
            xml.AppendLine("            </rows>");
            xml.AppendLine("          </section>");
            xml.AppendLine("        </sections>");
            xml.AppendLine("      </column></columns>");
            xml.AppendLine("    </tab>");
        }

        // Tab 2 - Details
        if (tab2Fields.Count > 0 && !formType.Equals("quickCreate", StringComparison.OrdinalIgnoreCase))
        {
            var tabId = NewGuid();
            var sectionId = NewGuid();
            xml.AppendLine($"    <tab verticallayout=\"true\" id=\"{tabId}\" IsUserDefined=\"1\">");
            xml.AppendLine($"      <labels><label description=\"Details\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine($"          <section showlabel=\"false\" showbar=\"false\" IsUserDefined=\"0\" id=\"{sectionId}\">");
            xml.AppendLine($"            <labels><label description=\"Details\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows>");
            foreach (var field in tab2Fields)
            {
                AppendFieldCell(xml, field, fieldDisplayNames, indent: "              ");
            }
            xml.AppendLine("            </rows>");
            xml.AppendLine("          </section>");
            xml.AppendLine("        </sections>");
            xml.AppendLine("      </column></columns>");
            xml.AppendLine("    </tab>");
        }

        // Tab 3 - Related (subgrids)
        if (tab3Subgrids.Count > 0 && !formType.Equals("quickCreate", StringComparison.OrdinalIgnoreCase))
        {
            var tabId = NewGuid();
            xml.AppendLine($"    <tab verticallayout=\"true\" id=\"{tabId}\" IsUserDefined=\"1\">");
            xml.AppendLine($"      <labels><label description=\"Related\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            foreach (var subgrid in tab3Subgrids)
            {
                var sectionId = NewGuid();
                var subgridUniqueId = NewGuid();
                xml.AppendLine($"          <section showlabel=\"true\" showbar=\"false\" IsUserDefined=\"0\" id=\"{sectionId}\">");
                xml.AppendLine($"            <labels><label description=\"{XmlEscape(subgrid)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                xml.AppendLine($"            <rows><row><cell id=\"{NewGuid()}\">");
                xml.AppendLine($"              <labels><label description=\"{XmlEscape(subgrid)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                xml.AppendLine($"              <control id=\"subgrid_{subgrid}\" classid=\"{{E7A81278-8635-4D9E-8D4D-59480B391C5B}}\" uniqueid=\"{subgridUniqueId}\">");
                xml.AppendLine("                <parameters>");
                xml.AppendLine($"                  <TargetEntityType>{XmlEscape(subgrid)}</TargetEntityType>");
                xml.AppendLine("                  <ViewIds>{00000000-0000-0000-0000-000000000000}</ViewIds>");
                xml.AppendLine("                </parameters>");
                xml.AppendLine("              </control>");
                xml.AppendLine("            </cell></row></rows>");
                xml.AppendLine($"          </section>");
            }
            xml.AppendLine("        </sections>");
            xml.AppendLine("      </column></columns>");
            xml.AppendLine("    </tab>");
        }

        // Tab 4 - Timeline (Notes control). The real Dataverse-emitted form uses
        // classid {06375649-c143-495e-a496-c962e5b4488e} with parameter
        // DefaultTabId=NotesTab — that matches what the maker portal generates
        // when you toggle "Show timeline". The legacy {B603CF9F-...} we used
        // before is a different control and produces a "broken" timeline cell.
        if (includeTimeline && !formType.Equals("quickCreate", StringComparison.OrdinalIgnoreCase))
        {
            var tabId = NewGuid();
            var sectionId = NewGuid();
            xml.AppendLine($"    <tab verticallayout=\"true\" id=\"{tabId}\" IsUserDefined=\"1\">");
            xml.AppendLine($"      <labels><label description=\"Timeline\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine($"          <section showlabel=\"false\" showbar=\"false\" IsUserDefined=\"0\" id=\"{sectionId}\" height=\"auto\">");
            xml.AppendLine($"            <labels><label description=\"Notes\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows>");
            xml.AppendLine($"              <row><cell showlabel=\"false\" rowspan=\"15\" auto=\"false\" id=\"{NewGuid()}\">");
            xml.AppendLine($"                <labels><label description=\"Note Text\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine($"                <control id=\"notescontrol\" classid=\"{{06375649-C143-495E-A496-C962E5B4488E}}\">");
            xml.AppendLine("                  <parameters><DefaultTabId>NotesTab</DefaultTabId></parameters>");
            xml.AppendLine("                </control>");
            xml.AppendLine("              </cell></row>");
            // 14 spacer rows to give the notes control vertical room (matches
            // what Dataverse generates when rowspan="15" is set on a cell).
            for (var i = 0; i < 14; i++)
            {
                xml.AppendLine("              <row />");
            }
            xml.AppendLine("              <row height=\"auto\" />");
            xml.AppendLine("            </rows>");
            xml.AppendLine("          </section>");
            xml.AppendLine("        </sections>");
            xml.AppendLine("      </column></columns>");
            xml.AppendLine("    </tab>");
        }

        xml.AppendLine("  </tabs>");

        // Header — sibling of <tabs>, AFTER it. Past version emitted <header>
        // BEFORE <tabs>; the form schema accepts both, but Dataverse's own
        // emitter places it after, and a couple of header-only attributes
        // (celllabelposition, labelwidth) only round-trip when the element
        // is in this canonical position.
        if (headerFields.Count > 0)
        {
            xml.AppendLine($"  <header id=\"{NewGuid()}\" celllabelposition=\"Top\" columns=\"111\" labelwidth=\"115\" celllabelalignment=\"Left\">");
            xml.AppendLine("    <rows>");
            xml.AppendLine("      <row>");
            // Header has 3 column tracks (columns="111"); we render the first
            // up-to-3 fields one per cell, padding the rest with empty cells
            // so the header always fills the row (Dataverse trims width
            // proportionally if cells are missing, which produces an off-center
            // header layout).
            var fieldsToRender = headerFields.Take(3).ToList();
            foreach (var field in fieldsToRender)
            {
                var displayName = fieldDisplayNames.TryGetValue(field, out var dn) ? dn : field;
                xml.AppendLine($"        <cell id=\"{NewGuid()}\">");
                xml.AppendLine($"          <labels><label description=\"{XmlEscape(displayName)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                xml.AppendLine($"          <control id=\"header_{field}\" classid=\"{{4273EDBD-AC1D-40D3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" />");
                xml.AppendLine("        </cell>");
            }
            for (var i = fieldsToRender.Count; i < 3; i++)
            {
                xml.AppendLine($"        <cell id=\"{NewGuid()}\" showlabel=\"false\">");
                xml.AppendLine($"          <labels><label description=\"\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                xml.AppendLine("        </cell>");
            }
            xml.AppendLine("      </row>");
            xml.AppendLine("    </rows>");
            xml.AppendLine("  </header>");
        }

        // Footer — empty 3-cell row matches the Dataverse default. Always
        // emitted; Dataverse expects the element to exist on Main forms.
        if (formType.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            xml.AppendLine($"  <footer id=\"{NewGuid()}\" celllabelposition=\"Top\" columns=\"111\" labelwidth=\"115\" celllabelalignment=\"Left\">");
            xml.AppendLine("    <rows>");
            xml.AppendLine("      <row>");
            for (var i = 0; i < 3; i++)
            {
                xml.AppendLine($"        <cell id=\"{NewGuid()}\" showlabel=\"false\">");
                xml.AppendLine($"          <labels><label description=\"\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                xml.AppendLine("        </cell>");
            }
            xml.AppendLine("      </row>");
            xml.AppendLine("    </rows>");
            xml.AppendLine("  </footer>");
        }

        xml.AppendLine("</form>");

        return xml.ToString();
    }

    /// <summary>
    /// Emits a single field cell with its display-name label. Matches the
    /// shape Dataverse itself generates: row → cell with id and labels →
    /// control with id, classid, datafieldname.
    /// </summary>
    private static void AppendFieldCell(
        System.Text.StringBuilder xml,
        string field,
        IDictionary<string, string> fieldDisplayNames,
        string indent)
    {
        var displayName = fieldDisplayNames.TryGetValue(field, out var dn) ? dn : field;
        var cellId = NewGuid();
        // Special-case the system Owner control — it uses a different classid
        // (Customer/Owner type) than the standard text/lookup control.
        var classId = field.Equals("ownerid", StringComparison.OrdinalIgnoreCase)
            ? "{270BD3DB-D9AF-4782-9025-509E298DEC0A}"
            : "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}";
        xml.AppendLine($"{indent}<row>");
        xml.AppendLine($"{indent}  <cell id=\"{cellId}\">");
        xml.AppendLine($"{indent}    <labels><label description=\"{XmlEscape(displayName)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
        xml.AppendLine($"{indent}    <control id=\"{field}\" classid=\"{classId}\" datafieldname=\"{field}\" />");
        xml.AppendLine($"{indent}  </cell>");
        xml.AppendLine($"{indent}</row>");
    }

    private static string XmlEscape(string s)
        => string.IsNullOrEmpty(s) ? string.Empty :
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Format Dataverse expects in formxml: braced uppercase GUID like "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}".
    /// </summary>
    private static string NewGuid() => Guid.NewGuid().ToString("B").ToUpperInvariant();

    /// <summary>
    /// Re-export so PlanUpdateFormTool can call the same builder with a parsed spec.
    /// </summary>
    internal static string BuildStructuredFormXmlPublic(
        string tableLogicalName,
        string formName,
        string formType,
        FormStructureSpec spec,
        List<string> headerFields,
        IDictionary<string, string> fieldDisplayNames)
        => BuildStructuredFormXml(tableLogicalName, formName, formType, spec, headerFields, fieldDisplayNames);

    /// <summary>
    /// Same emission shape as <see cref="BuildFormXml"/> but driven by an
    /// arbitrary tabs/columns/sections/cells tree rather than the fixed
    /// 3-tab layout. Re-uses <see cref="AppendFieldCell"/>, the header /
    /// footer emitter, and the Notes/Timeline emitter so the output matches
    /// what Dataverse itself produces.
    /// </summary>
    internal static string BuildStructuredFormXml(
        string tableLogicalName,
        string formName,
        string formType,
        FormStructureSpec spec,
        List<string> headerFields,
        IDictionary<string, string> fieldDisplayNames)
    {
        _ = tableLogicalName; _ = formName;  // referenced only by callers / kept for symmetry

        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<form headerdensity=\"HighWithControls\">");

        // Phase 6: <ancestor> is FormType's first child in the schema's xs:all,
        // so emit it before <tabs> when set. Dataverse uses this to track
        // lineage when a form derives from another (e.g. quick-view of main).
        if (!string.IsNullOrWhiteSpace(spec.AncestorFormId))
        {
            xml.AppendLine($"  <ancestor id=\"{XmlEscape(spec.AncestorFormId!)}\" />");
        }

        xml.AppendLine("  <tabs>");

        foreach (var tab in spec.Tabs)
        {
            var tabId = NewGuid();
            var tabLabel = string.IsNullOrWhiteSpace(tab.Label) ? tab.Name : tab.Label!;
            var tabAttrs = new List<string>
            {
                $"id=\"{tabId}\"",
                "IsUserDefined=\"1\"",
                $"verticallayout=\"{(tab.VerticalLayout ? "true" : "false")}\"",
            };
            if (!tab.Expanded) tabAttrs.Add("expanded=\"false\"");
            if (!tab.Visible) tabAttrs.Add("visible=\"false\"");
            if (!tab.ShowLabel) tabAttrs.Add("showlabel=\"false\"");
            if (!tab.AvailableForPhone) tabAttrs.Add("availableforphone=\"false\"");

            xml.AppendLine($"    <tab {string.Join(' ', tabAttrs)}>");
            xml.AppendLine($"      <labels><label description=\"{XmlEscape(tabLabel)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns>");
            foreach (var col in tab.Columns)
            {
                xml.AppendLine($"        <column width=\"{col.Width}\">");
                xml.AppendLine("          <sections>");
                foreach (var section in col.Sections)
                {
                    var sectionId = NewGuid();
                    var sectionLabel = string.IsNullOrWhiteSpace(section.Label) ? section.Name : section.Label!;
                    var sectionAttrs = new List<string>
                    {
                        $"id=\"{sectionId}\"",
                        "IsUserDefined=\"0\"",
                        $"showlabel=\"{(section.ShowLabel ? "true" : "false")}\"",
                        "showbar=\"false\"",
                    };
                    if (!section.Visible) sectionAttrs.Add("visible=\"false\"");
                    if (!section.AvailableForPhone) sectionAttrs.Add("availableforphone=\"false\"");
                    if (section.Columns > 1)
                    {
                        // Section's grid of cell columns is encoded as "111" / "11" etc.
                        // Each `1` is one column track; total characters = column count.
                        sectionAttrs.Add($"columns=\"{new string('1', Math.Clamp(section.Columns, 1, 4))}\"");
                    }

                    xml.AppendLine($"            <section {string.Join(' ', sectionAttrs)}>");
                    xml.AppendLine($"              <labels><label description=\"{XmlEscape(sectionLabel)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                    xml.AppendLine("              <rows>");
                    foreach (var cell in section.Cells)
                    {
                        AppendStructuredCell(xml, cell, fieldDisplayNames, indent: "                ");
                    }
                    xml.AppendLine("              </rows>");
                    xml.AppendLine("            </section>");
                }
                xml.AppendLine("          </sections>");
                xml.AppendLine("        </column>");
            }
            xml.AppendLine("      </columns>");
            xml.AppendLine("    </tab>");
        }

        // Optional Timeline tab — same control as the legacy path.
        if (spec.IncludeTimeline && !formType.Equals("quickCreate", StringComparison.OrdinalIgnoreCase))
        {
            EmitTimelineTab(xml);
        }

        xml.AppendLine("  </tabs>");

        // Header (sibling of <tabs>, after) — same emission as legacy path.
        if (headerFields.Count > 0)
        {
            EmitHeader(xml, headerFields, fieldDisplayNames);
        }

        if (formType.Equals("main", StringComparison.OrdinalIgnoreCase))
        {
            EmitEmptyFooter(xml);
        }

        // Phase 5: form-level events + library declarations. Order matters per
        // FormXml.xsd's <xs:all>: Dataverse's own emitter places <events> right
        // after <footer> and <formLibraries> at the very end. Stick to that.
        EmitEvents(xml, spec.Events);
        EmitFormLibraries(xml, spec.Libraries);

        // Phase 6: role-gated form display conditions.
        EmitDisplayConditions(xml, spec.DisplayConditions);

        xml.AppendLine("</form>");
        return xml.ToString();
    }

    /// <summary>
    /// Emits one cell in the structured layout. Branches on ControlType:
    /// "default" → standard data control; "owner" → Owner classid; "subgrid" →
    /// related-records grid keyed off TargetEntity.
    /// </summary>
    private static void AppendStructuredCell(
        System.Text.StringBuilder xml,
        FormCellSpec cell,
        IDictionary<string, string> fieldDisplayNames,
        string indent)
    {
        var cellId = NewGuid();
        var cellAttrs = new List<string> { $"id=\"{cellId}\"" };
        if (!cell.ShowLabel) cellAttrs.Add("showlabel=\"false\"");
        if (!cell.Visible) cellAttrs.Add("visible=\"false\"");
        if (!cell.AvailableForPhone) cellAttrs.Add("availableforphone=\"false\"");
        if (cell.Rowspan > 1) cellAttrs.Add($"rowspan=\"{cell.Rowspan}\"");
        if (cell.Colspan > 1) cellAttrs.Add($"colspan=\"{cell.Colspan}\"");

        xml.AppendLine($"{indent}<row>");
        xml.AppendLine($"{indent}  <cell {string.Join(' ', cellAttrs)}>");

        if (string.Equals(cell.ControlType, "subgrid", StringComparison.OrdinalIgnoreCase))
        {
            var target = (cell.TargetEntity ?? string.Empty).ToLowerInvariant();
            var label = string.IsNullOrWhiteSpace(target) ? "Related" : target;
            xml.AppendLine($"{indent}    <labels><label description=\"{XmlEscape(label)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine($"{indent}    <control id=\"subgrid_{XmlEscape(target)}\" classid=\"{{E7A81278-8635-4D9E-8D4D-59480B391C5B}}\" uniqueid=\"{NewGuid()}\">");
            xml.AppendLine($"{indent}      <parameters>");
            xml.AppendLine($"{indent}        <TargetEntityType>{XmlEscape(target)}</TargetEntityType>");
            if (!string.IsNullOrWhiteSpace(cell.RelationshipName))
                xml.AppendLine($"{indent}        <RelationshipName>{XmlEscape(cell.RelationshipName)}</RelationshipName>");
            // Only emit ViewId when explicitly provided — Dataverse picks the
            // entity's default view when omitted, which is usually the right call.
            if (!string.IsNullOrWhiteSpace(cell.ViewId))
            {
                xml.AppendLine($"{indent}        <ViewId>{XmlEscape(cell.ViewId)}</ViewId>");
            }
            if (!string.IsNullOrWhiteSpace(cell.ViewIds))
            {
                // Multi-view picker. Dataverse expects a comma-separated list
                // of {GUID} values. We don't reformat — pass through whatever
                // the agent provided after a light trim.
                xml.AppendLine($"{indent}        <ViewIds>{XmlEscape(cell.ViewIds.Trim())}</ViewIds>");
            }
            xml.AppendLine($"{indent}        <EnableQuickFind>{(cell.EnableQuickFind ? "true" : "false")}</EnableQuickFind>");
            xml.AppendLine($"{indent}        <EnableViewPicker>{(cell.EnableViewPicker ? "true" : "false")}</EnableViewPicker>");
            // Optional / nullable params — only emit when the agent set them
            // explicitly so we don't pollute Dataverse's defaults.
            if (!string.IsNullOrWhiteSpace(cell.AutoExpand))
                xml.AppendLine($"{indent}        <AutoExpand>{XmlEscape(cell.AutoExpand)}</AutoExpand>");
            if (cell.RecordsPerPage is int recordsPerPage)
                xml.AppendLine($"{indent}        <RecordsPerPage>{recordsPerPage}</RecordsPerPage>");
            if (cell.MaxRowsBeforeScroll is int maxRows)
                xml.AppendLine($"{indent}        <MaxRowsBeforeScroll>{maxRows}</MaxRowsBeforeScroll>");
            if (cell.EnableJumpBar is bool jumpBar)
                xml.AppendLine($"{indent}        <EnableJumpBar>{(jumpBar ? "true" : "false")}</EnableJumpBar>");
            if (!string.IsNullOrWhiteSpace(cell.ChartGridMode))
                xml.AppendLine($"{indent}        <ChartGridMode>{XmlEscape(cell.ChartGridMode)}</ChartGridMode>");
            if (!string.IsNullOrWhiteSpace(cell.VisualizationId))
                xml.AppendLine($"{indent}        <VisualizationId>{XmlEscape(cell.VisualizationId)}</VisualizationId>");
            if (cell.EnableChartPicker is bool chartPicker)
                xml.AppendLine($"{indent}        <EnableChartPicker>{(chartPicker ? "true" : "false")}</EnableChartPicker>");
            if (cell.EnableContextualActions is bool ctxActions)
                xml.AppendLine($"{indent}        <EnableContextualActions>{(ctxActions ? "true" : "false")}</EnableContextualActions>");
            xml.AppendLine($"{indent}      </parameters>");
            xml.AppendLine($"{indent}    </control>");
        }
        else if (string.Equals(cell.ControlType, "webresource", StringComparison.OrdinalIgnoreCase))
        {
            // WebResource cell — different classid + isunbound. Optional Field
            // is allowed but typically not set; the WebResourceId in Params is
            // what binds the cell to a JS / HTML resource.
            var resourceId = cell.Params?.WebResourceId ?? string.Empty;
            var label = !string.IsNullOrWhiteSpace(cell.Params?.AltText) ? cell.Params!.AltText! : "Web Resource";
            xml.AppendLine($"{indent}    <labels><label description=\"{XmlEscape(label!)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine($"{indent}    <control id=\"webresource_{NewControlSlug()}\" classid=\"{{9FDF5F91-88B1-47F4-AD53-C11EFC01A01D}}\" isunbound=\"true\">");
            xml.AppendLine($"{indent}      <parameters>");
            xml.AppendLine($"{indent}        <WebResourceId>{XmlEscape(resourceId)}</WebResourceId>");
            EmitOptional(xml, indent, "Url", cell.Params?.Url);
            EmitOptional(xml, indent, "Height", cell.Params?.Height);
            EmitOptional(xml, indent, "Width", cell.Params?.Width);
            EmitOptional(xml, indent, "AltText", cell.Params?.AltText);
            EmitOptional(xml, indent, "SizeType", cell.Params?.SizeType);
            EmitOptional(xml, indent, "HorizontalAlignment", cell.Params?.HorizontalAlignment);
            EmitOptional(xml, indent, "VerticalAlignment", cell.Params?.VerticalAlignment);
            EmitOptionalBool(xml, indent, "ShowOnMobileClient", cell.Params?.ShowOnMobileClient);
            EmitOptionalBool(xml, indent, "Scrolling", cell.Params?.Scrolling);
            EmitOptionalBool(xml, indent, "Border", cell.Params?.Border);
            EmitOptionalBool(xml, indent, "PassParameters", cell.Params?.PassParameters);
            EmitOptionalBool(xml, indent, "Security", cell.Params?.Security);
            xml.AppendLine($"{indent}      </parameters>");
            xml.AppendLine($"{indent}    </control>");
        }
        else if (string.Equals(cell.ControlType, "quickform", StringComparison.OrdinalIgnoreCase))
        {
            // Quick Form cell — embeds one or more quick view forms. Bound to
            // a lookup field when Field is set; otherwise unbound.
            var field = (cell.Field ?? string.Empty).ToLowerInvariant();
            var quickForms = cell.Params?.QuickForms ?? string.Empty;
            var displayName = string.IsNullOrEmpty(field)
                ? "Quick Form"
                : (fieldDisplayNames.TryGetValue(field, out var dn) ? dn : field);
            xml.AppendLine($"{indent}    <labels><label description=\"{XmlEscape(displayName)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            var bindAttrs = string.IsNullOrEmpty(field)
                ? "isunbound=\"true\""
                : $"datafieldname=\"{XmlEscape(field)}\"";
            xml.AppendLine($"{indent}    <control id=\"quickform_{NewControlSlug()}\" classid=\"{{069810AC-4998-4D33-AAF1-1841F7544F95}}\" {bindAttrs}>");
            xml.AppendLine($"{indent}      <parameters>");
            xml.AppendLine($"{indent}        <QuickForms>{XmlEscape(quickForms)}</QuickForms>");
            EmitOptional(xml, indent, "ControlMode", cell.Params?.ControlMode);
            xml.AppendLine($"{indent}      </parameters>");
            xml.AppendLine($"{indent}    </control>");
        }
        else
        {
            // default / owner / lookup — share the data-bound shape with the
            // standard data-control classid. Owner uses a special classid;
            // lookup just signals intent so the agent's params get a
            // strongly-typed home.
            var field = (cell.Field ?? string.Empty).ToLowerInvariant();
            var displayName = fieldDisplayNames.TryGetValue(field, out var dn) ? dn : field;
            var classId = string.Equals(cell.ControlType, "owner", StringComparison.OrdinalIgnoreCase)
                ? "{270BD3DB-D9AF-4782-9025-509E298DEC0A}"
                : (field.Equals("ownerid", StringComparison.OrdinalIgnoreCase)
                    ? "{270BD3DB-D9AF-4782-9025-509E298DEC0A}"
                    : "{4273EDBD-AC1D-40D3-9FB2-095C621B552D}");
            var disabledAttr = cell.Disabled ? " disabled=\"true\"" : "";
            xml.AppendLine($"{indent}    <labels><label description=\"{XmlEscape(displayName)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");

            // Emit a <parameters> block ONLY if the agent set any param
            // worth sending. Empty parameter blocks bloat the FormXML for no
            // reason, and Dataverse picks sensible defaults from the
            // attribute's metadata when the block is absent.
            if (HasAnyDataParams(cell.Params))
            {
                xml.AppendLine($"{indent}    <control id=\"{XmlEscape(field)}\" classid=\"{classId}\" datafieldname=\"{XmlEscape(field)}\"{disabledAttr}>");
                xml.AppendLine($"{indent}      <parameters>");
                EmitOptional(xml, indent, "Format", cell.Params?.Format);
                EmitOptional(xml, indent, "MaxLength", cell.Params?.MaxLength);
                EmitOptional(xml, indent, "MinValue", cell.Params?.MinValue);
                EmitOptional(xml, indent, "MaxValue", cell.Params?.MaxValue);
                EmitOptional(xml, indent, "Precision", cell.Params?.Precision);
                EmitOptional(xml, indent, "DefaultValue", cell.Params?.DefaultValue);
                EmitOptional(xml, indent, "DefaultViewId", cell.Params?.DefaultViewId);
                EmitOptional(xml, indent, "FilterRelationshipName", cell.Params?.FilterRelationshipName);
                EmitOptional(xml, indent, "DependentAttributeName", cell.Params?.DependentAttributeName);
                EmitOptional(xml, indent, "DependentAttributeType", cell.Params?.DependentAttributeType);
                EmitOptionalBool(xml, indent, "DisableMru", cell.Params?.DisableMru);
                EmitOptionalBool(xml, indent, "DisableQuickFind", cell.Params?.DisableQuickFind);
                EmitOptionalBool(xml, indent, "DisableViewPicker", cell.Params?.DisableViewPicker);
                EmitOptionalBool(xml, indent, "IsInlineNewEnabled", cell.Params?.IsInlineNewEnabled);
                EmitOptional(xml, indent, "AvailableViewIds", cell.Params?.AvailableViewIds);
                xml.AppendLine($"{indent}      </parameters>");
                xml.AppendLine($"{indent}    </control>");
            }
            else
            {
                xml.AppendLine($"{indent}    <control id=\"{XmlEscape(field)}\" classid=\"{classId}\" datafieldname=\"{XmlEscape(field)}\"{disabledAttr} />");
            }
        }

        xml.AppendLine($"{indent}  </cell>");
        xml.AppendLine($"{indent}</row>");
    }

    /// <summary>
    /// Emit a single optional parameter when the value is non-null /
    /// non-empty. Keeps the call site readable when there are 10+ optional
    /// fields to consider.
    /// </summary>
    private static void EmitOptional(System.Text.StringBuilder xml, string indent, string elementName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        xml.AppendLine($"{indent}        <{elementName}>{XmlEscape(value)}</{elementName}>");
    }

    private static void EmitOptional(System.Text.StringBuilder xml, string indent, string elementName, int? value)
    {
        if (value is not int v) return;
        xml.AppendLine($"{indent}        <{elementName}>{v}</{elementName}>");
    }

    private static void EmitOptional(System.Text.StringBuilder xml, string indent, string elementName, double? value)
    {
        if (value is not double v) return;
        // Use invariant culture so "12.5" doesn't become "12,5" on FR locales.
        xml.AppendLine($"{indent}        <{elementName}>{v.ToString(System.Globalization.CultureInfo.InvariantCulture)}</{elementName}>");
    }

    private static void EmitOptionalBool(System.Text.StringBuilder xml, string indent, string elementName, bool? value)
    {
        if (value is not bool v) return;
        xml.AppendLine($"{indent}        <{elementName}>{(v ? "true" : "false")}</{elementName}>");
    }

    /// <summary>
    /// Whether any of the typed data-control params on the cell is set. Used
    /// by the default/owner/lookup branch to decide whether to emit a
    /// <c>&lt;parameters&gt;</c> block at all (empty blocks bloat FormXML).
    /// </summary>
    private static bool HasAnyDataParams(FormCellParams? p)
    {
        if (p is null) return false;
        return !string.IsNullOrWhiteSpace(p.Format)
            || p.MaxLength is not null
            || p.MinValue is not null
            || p.MaxValue is not null
            || p.Precision is not null
            || !string.IsNullOrWhiteSpace(p.DefaultValue)
            || !string.IsNullOrWhiteSpace(p.DefaultViewId)
            || !string.IsNullOrWhiteSpace(p.FilterRelationshipName)
            || !string.IsNullOrWhiteSpace(p.DependentAttributeName)
            || !string.IsNullOrWhiteSpace(p.DependentAttributeType)
            || p.DisableMru is not null
            || p.DisableQuickFind is not null
            || p.DisableViewPicker is not null
            || p.IsInlineNewEnabled is not null
            || !string.IsNullOrWhiteSpace(p.AvailableViewIds);
    }

    /// <summary>
    /// Slug used in control ids when there's no field to anchor on. Web
    /// resources and quick forms can appear multiple times on a form so the
    /// id needs to be unique per cell (Dataverse rejects duplicate ids).
    /// </summary>
    private static string NewControlSlug() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Notes / Timeline tab, extracted so both legacy and structured paths emit
    /// the same shape (matching what Dataverse generates when "Show timeline" is
    /// toggled in the maker portal).
    /// </summary>
    private static void EmitTimelineTab(System.Text.StringBuilder xml)
    {
        var tabId = NewGuid();
        var sectionId = NewGuid();
        xml.AppendLine($"    <tab verticallayout=\"true\" id=\"{tabId}\" IsUserDefined=\"1\">");
        xml.AppendLine($"      <labels><label description=\"Timeline\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
        xml.AppendLine("      <columns><column width=\"100%\">");
        xml.AppendLine("        <sections>");
        xml.AppendLine($"          <section showlabel=\"false\" showbar=\"false\" IsUserDefined=\"0\" id=\"{sectionId}\" height=\"auto\">");
        xml.AppendLine($"            <labels><label description=\"Notes\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
        xml.AppendLine("            <rows>");
        xml.AppendLine($"              <row><cell showlabel=\"false\" rowspan=\"15\" auto=\"false\" id=\"{NewGuid()}\">");
        xml.AppendLine($"                <labels><label description=\"Note Text\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
        xml.AppendLine($"                <control id=\"notescontrol\" classid=\"{{06375649-C143-495E-A496-C962E5B4488E}}\">");
        xml.AppendLine("                  <parameters><DefaultTabId>NotesTab</DefaultTabId></parameters>");
        xml.AppendLine("                </control>");
        xml.AppendLine("              </cell></row>");
        for (var i = 0; i < 14; i++) xml.AppendLine("              <row />");
        xml.AppendLine("              <row height=\"auto\" />");
        xml.AppendLine("            </rows>");
        xml.AppendLine("          </section>");
        xml.AppendLine("        </sections>");
        xml.AppendLine("      </column></columns>");
        xml.AppendLine("    </tab>");
    }

    /// <summary>
    /// Header block (sibling of tabs). Renders up to 3 fields one-per-cell,
    /// padding the rest with empty cells so the row always fills the
    /// `columns="111"` track count.
    /// </summary>
    private static void EmitHeader(
        System.Text.StringBuilder xml,
        List<string> headerFields,
        IDictionary<string, string> fieldDisplayNames)
    {
        xml.AppendLine($"  <header id=\"{NewGuid()}\" celllabelposition=\"Top\" columns=\"111\" labelwidth=\"115\" celllabelalignment=\"Left\">");
        xml.AppendLine("    <rows>");
        xml.AppendLine("      <row>");
        var fieldsToRender = headerFields.Take(3).ToList();
        foreach (var field in fieldsToRender)
        {
            var displayName = fieldDisplayNames.TryGetValue(field, out var dn) ? dn : field;
            xml.AppendLine($"        <cell id=\"{NewGuid()}\">");
            xml.AppendLine($"          <labels><label description=\"{XmlEscape(displayName)}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine($"          <control id=\"header_{field}\" classid=\"{{4273EDBD-AC1D-40D3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" />");
            xml.AppendLine("        </cell>");
        }
        for (var i = fieldsToRender.Count; i < 3; i++)
        {
            xml.AppendLine($"        <cell id=\"{NewGuid()}\" showlabel=\"false\">");
            xml.AppendLine($"          <labels><label description=\"\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("        </cell>");
        }
        xml.AppendLine("      </row>");
        xml.AppendLine("    </rows>");
        xml.AppendLine("  </header>");
    }

    /// <summary>
    /// Emits <c>&lt;events&gt;</c> with one <c>&lt;event&gt;</c> per binding. The
    /// schema requires each event to carry a <c>&lt;Handlers&gt;&lt;Handler/&gt;</c>
    /// child describing the JS function + library, plus a stable
    /// <c>handlerUniqueId</c>. We default <c>active="true"</c>,
    /// <c>passExecutionContext</c> to the user's choice (default true).
    /// </summary>
    private static void EmitEvents(System.Text.StringBuilder xml, IReadOnlyList<FormEventSpec>? events)
    {
        if (events is null || events.Count == 0) return;

        xml.AppendLine("  <events>");
        foreach (var ev in events)
        {
            // Build the event open tag attributes. ControlEvent needs the
            // attribute or control id; DataEvent doesn't.
            var attrs = new List<string>
            {
                $"name=\"{XmlEscape((ev.Name ?? "").ToLowerInvariant())}\"",
                $"active=\"{(ev.Enabled ? "true" : "false")}\"",
                $"eventType=\"{XmlEscape(ev.EventType ?? "DataEvent")}\""
            };
            if (string.Equals(ev.EventType, "ControlEvent", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(ev.Attribute))
                    attrs.Add($"attribute=\"{XmlEscape(ev.Attribute!)}\"");
                if (!string.IsNullOrWhiteSpace(ev.Control))
                    attrs.Add($"control=\"{XmlEscape(ev.Control!)}\"");
            }
            xml.AppendLine($"    <event {string.Join(' ', attrs)}>");
            xml.AppendLine("      <Handlers>");
            xml.AppendLine($"        <Handler functionName=\"{XmlEscape(ev.FunctionName ?? "")}\" "
                + $"libraryName=\"{XmlEscape(ev.LibraryName ?? "")}\" "
                + $"handlerUniqueId=\"{NewGuid()}\" "
                + $"enabled=\"{(ev.Enabled ? "true" : "false")}\" "
                + $"passExecutionContext=\"{(ev.PassExecutionContext ? "true" : "false")}\""
                + (string.IsNullOrWhiteSpace(ev.Parameters) ? "" : $" parameters=\"{XmlEscape(ev.Parameters!)}\"")
                + " />");
            xml.AppendLine("      </Handlers>");
            xml.AppendLine("    </event>");
        }
        xml.AppendLine("  </events>");
    }

    /// <summary>
    /// Emits <c>&lt;DisplayConditions&gt;</c> — role-gated form selection. The
    /// schema's xs:choice means we emit either <c>&lt;Everyone/&gt;</c> OR a
    /// list of <c>&lt;Role Id="…"/&gt;</c>, never both. Optional FallbackForm
    /// + Order land on the wrapping element.
    /// </summary>
    private static void EmitDisplayConditions(System.Text.StringBuilder xml, FormDisplayConditionsSpec? dc)
    {
        if (dc is null) return;

        var attrs = new List<string>();
        if (dc.FallbackForm is bool fb) attrs.Add($"FallbackForm=\"{(fb ? "true" : "false")}\"");
        if (dc.Order is int ord) attrs.Add($"Order=\"{ord}\"");
        var attrString = attrs.Count > 0 ? " " + string.Join(' ', attrs) : "";

        xml.AppendLine($"  <DisplayConditions{attrString}>");
        if (dc.Everyone)
        {
            xml.AppendLine("    <Everyone />");
        }
        else if (dc.RoleIds is { Count: > 0 })
        {
            foreach (var roleId in dc.RoleIds)
            {
                xml.AppendLine($"    <Role Id=\"{XmlEscape(roleId)}\" />");
            }
        }
        xml.AppendLine("  </DisplayConditions>");
    }

    /// <summary>
    /// Emits <c>&lt;formLibraries&gt;</c> — the JS web resources the form
    /// references. Required for any <c>&lt;event&gt;</c> handler to resolve.
    /// </summary>
    private static void EmitFormLibraries(System.Text.StringBuilder xml, IReadOnlyList<FormLibrarySpec>? libs)
    {
        if (libs is null || libs.Count == 0) return;

        xml.AppendLine("  <formLibraries>");
        foreach (var lib in libs)
        {
            xml.AppendLine($"    <Library name=\"{XmlEscape(lib.Name ?? "")}\" libraryUniqueId=\"{XmlEscape(lib.UniqueId ?? "")}\" />");
        }
        xml.AppendLine("  </formLibraries>");
    }

    private static void EmitEmptyFooter(System.Text.StringBuilder xml)
    {
        xml.AppendLine($"  <footer id=\"{NewGuid()}\" celllabelposition=\"Top\" columns=\"111\" labelwidth=\"115\" celllabelalignment=\"Left\">");
        xml.AppendLine("    <rows>");
        xml.AppendLine("      <row>");
        for (var i = 0; i < 3; i++)
        {
            xml.AppendLine($"        <cell id=\"{NewGuid()}\" showlabel=\"false\">");
            xml.AppendLine($"          <labels><label description=\"\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("        </cell>");
        }
        xml.AppendLine("      </row>");
        xml.AppendLine("    </rows>");
        xml.AppendLine("  </footer>");
    }
}
