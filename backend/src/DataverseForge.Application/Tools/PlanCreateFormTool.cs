using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext)
{
    [Description("Generate a plan for creating a Dataverse form. Returns a planId, the FormXML preview, "
        + "warnings, and risk level. The full OData JSON payload is stored server-side under planId so it "
        + "cannot be mangled. Pass the planId verbatim to execute_create_form after approval. Validates "
        + "that every field name actually exists on the table — call get_table_details first if you're "
        + "unsure of logical names. For multi-column / multi-section layouts pass tabsJson — when "
        + "non-empty it overrides the simple tab1Fields/tab2Fields/tab3Subgrids inputs.")]
    public async Task<FormCreationPlan> PlanCreateFormAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("The display name for the form (e.g., 'Work Order Main Form')")] string formName,
        [Description("The form type: 'main', 'quickCreate', 'quickView', or 'card'. Card forms (type 11) are the compact tile layouts shown in the new mobile/UCI experience — they should be single-tab with a small number of fields.")] string formType,
        [Description("Description of the form")] string description,
        [Description("Comma-separated list of field LOGICAL names for tab 1. Use lowercase logical names (e.g., 'eppc_giftname,eppc_description'), NOT display names. System fields like 'statuscode' have no prefix. IGNORED when tabsJson is non-empty.")] string tab1Fields,
        [Description("Comma-separated list of field logical names for tab 2 (Details). IGNORED when tabsJson is non-empty.")] string tab2Fields,
        [Description("Comma-separated list of related entity logical names for subgrids in tab 3 (Related). IGNORED when tabsJson is non-empty.")] string tab3Subgrids,
        [Description("Whether to include a Timeline tab for notes and activities. Honored in both legacy and structured paths.")] bool includeTimeline,
        [Description("Comma-separated list of up to 4 field logical names for the header. IGNORED when tabsJson contains a 'headerFields' array.")] string headerFields,
        [Description("OPTIONAL structured layout. JSON shape: {tabs:[{name,label?,columns:[{width:'50%',sections:[{name,label?,cells:[{field:'logicalname',controlType:'default'|'owner'|'subgrid',rowspan?,colspan?,showLabel?,visible?,disabled?,targetEntity?,relationshipName?,viewId?}]}]}]}],headerFields?:['x','y'],includeTimeline?:true}. Column widths must be percentages summing to 100 per tab; 1-3 columns per tab; 1-100 tabs. When non-empty supersedes tab1/tab2/tab3 CSVs.")] string tabsJson,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Form type codes per the SystemForm.Type picklist (Dataverse SystemForm
        // entity reference). Past bug: "quickCreate" was mapped to 5, but 5 is
        // "Mobile - Express" — Quick Create is type 7.
        //   2 = Main, 6 = Quick View Form, 7 = Quick Create, 11 = Card
        var formTypeCode = formType.ToLowerInvariant() switch
        {
            "main" => 2,
            "quickcreate" => 7,
            "quickview" => 6,
            "card" => 11,
            _ => -1
        };

        if (formTypeCode == -1)
            warnings.Add($"Unrecognized form type '{formType}'. Expected 'main', 'quickCreate', 'quickView', or 'card'.");

        // Branch the inputs: when tabsJson is provided, the structured spec is
        // the source of truth. Otherwise we use the legacy CSV parameters. We
        // collapse both paths to a single FormStructureSpec downstream so the
        // builder has one shape to deal with.
        FormStructureSpec? structured = null;
        if (!string.IsNullOrWhiteSpace(tabsJson))
        {
            try
            {
                structured = ParseStructured(tabsJson, includeTimeline, headerFields);
            }
            catch (System.Text.Json.JsonException jx)
            {
                return new FormCreationPlan(
                    PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                    TableLogicalName: tableLogicalName,
                    FormName: formName,
                    FormType: formType,
                    Description: description,
                    FormXml: string.Empty,
                    WebApiPayloadJson: string.Empty,
                    Warnings: new List<string> { $"REJECTED: tabsJson is not valid JSON. {jx.Message}" }.AsReadOnly(),
                    RiskLevel: "red");
            }

            var structuredErrors = ValidateStructured(structured);
            if (structuredErrors.Count > 0)
            {
                return new FormCreationPlan(
                    PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                    TableLogicalName: tableLogicalName,
                    FormName: formName,
                    FormType: formType,
                    Description: description,
                    FormXml: string.Empty,
                    WebApiPayloadJson: string.Empty,
                    Warnings: new List<string> { "REJECTED: " + string.Join(" ", structuredErrors) }.AsReadOnly(),
                    RiskLevel: "red");
            }
        }

        var tab1FieldList = NormalizeFieldList(tab1Fields);
        var tab2FieldList = NormalizeFieldList(tab2Fields);
        var tab3SubgridList = NormalizeFieldList(tab3Subgrids);
        var headerFieldList = structured?.HeaderFields is { Count: > 0 } sh
            ? sh.Select(s => s.ToLowerInvariant()).ToList()
            : NormalizeFieldList(headerFields);

        // Validate every referenced field actually exists on the table. The agent has
        // historically passed PascalCase / hallucinated names (e.g., eppc_ContactId for
        // a missing lookup, eppc_StatusCode instead of system 'statuscode'); Dataverse
        // then rejects the whole form payload with a 400 and the agent loops on retry.
        // Returning a rejection plan here makes the failure mode unambiguous.
        var fieldDisplayNames = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var details = await dataverseClient.GetTableDetailsAsync(tableLogicalName, cancellationToken);
            var available = new HashSet<string>(
                details.Columns.Select(c => c.LogicalName.ToLowerInvariant()),
                StringComparer.Ordinal);
            foreach (var c in details.Columns)
            {
                fieldDisplayNames[c.LogicalName.ToLowerInvariant()] =
                    string.IsNullOrWhiteSpace(c.DisplayName) ? c.LogicalName : c.DisplayName;
            }

            // Pull every field reference (legacy CSVs + structured cells) into one set so
            // we report ALL missing names at once, not one-at-a-time.
            // Cells that are typically unbound (subgrid, webresource) or accept an
            // optional field (quickform — bound when targeting a lookup) skip this
            // check; bound data cells (default/owner/lookup) always validate.
            var unboundOrOptionalTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "subgrid", "webresource", "quickform"
            };
            var structuredFields = structured is null
                ? Enumerable.Empty<string>()
                : structured.Tabs
                    .SelectMany(t => t.Columns)
                    .SelectMany(c => c.Sections)
                    .SelectMany(s => s.Cells)
                    .Where(c => !unboundOrOptionalTypes.Contains(c.ControlType ?? "default"))
                    .Where(c => !string.IsNullOrWhiteSpace(c.Field))
                    .Select(c => c.Field!.ToLowerInvariant());

            var allReferenced = tab1FieldList
                .Concat(tab2FieldList)
                .Concat(headerFieldList)
                .Concat(structuredFields)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var missing = allReferenced.Where(f => !available.Contains(f)).ToList();
            if (missing.Count > 0)
            {
                var sample = string.Join(", ", available.OrderBy(s => s).Take(15));
                var rejection = new List<string>
                {
                    $"REJECTED: The following field(s) do NOT exist on table '{tableLogicalName}': "
                    + string.Join(", ", missing) + ". "
                    + "Form creation will fail at Dataverse with HTTP 400. "
                    + "Available logical names on this table (sample): " + sample + ". "
                    + "Either remove the missing fields, fix the spelling (logical names are lowercase), "
                    + "or call plan_create_field / plan_create_one_to_many_relationship FIRST to create them."
                };
                return new FormCreationPlan(
                    PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                    TableLogicalName: tableLogicalName,
                    FormName: formName,
                    FormType: formType,
                    Description: description,
                    FormXml: string.Empty,
                    WebApiPayloadJson: string.Empty,
                    Warnings: rejection.AsReadOnly(),
                    RiskLevel: "red");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hard-reject when the table itself doesn't exist. Past failure: agent
            // passed `tableLogicalName="gift"` (no prefix) — the table is actually
            // `eppc_gift`. The planner used to just WARN and produce a payload with
            // `objecttypecode:"gift"`, which Dataverse 400'd at execute time. Returning
            // a rejection plan here makes the failure mode unambiguous and prevents
            // the executor from sending a doomed payload.
            var rejection = new List<string>
            {
                $"REJECTED: Table '{tableLogicalName}' does not exist in this environment. "
                + "Form creation will fail. Custom tables typically have a publisher prefix "
                + "(e.g. 'eppc_gift', not 'gift'). Call get_tables_list(filter:'solution', solutionName:<active>) "
                + $"to confirm the correct logical name. Underlying lookup error: {ex.Message}"
            };
            return new FormCreationPlan(
                PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                TableLogicalName: tableLogicalName,
                FormName: formName,
                FormType: formType,
                Description: description,
                FormXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: rejection.AsReadOnly(),
                RiskLevel: "red");
        }

        // Quick create validation — count fields across whichever input shape is in use.
        if (formType.Equals("quickCreate", StringComparison.OrdinalIgnoreCase))
        {
            var totalFields = structured is null
                ? tab1FieldList.Count + tab2FieldList.Count
                : structured.Tabs.SelectMany(t => t.Columns).SelectMany(c => c.Sections).SelectMany(s => s.Cells).Count(c => !string.Equals(c.ControlType, "subgrid", StringComparison.OrdinalIgnoreCase));
            var subgridCount = structured is null
                ? tab3SubgridList.Count
                : structured.Tabs.SelectMany(t => t.Columns).SelectMany(c => c.Sections).SelectMany(s => s.Cells).Count(c => string.Equals(c.ControlType, "subgrid", StringComparison.OrdinalIgnoreCase));
            if (totalFields > 7)
                warnings.Add($"Quick Create forms should have max 5-7 fields. Current count: {totalFields}.");
            if (subgridCount > 0)
                warnings.Add("Quick Create forms should not contain subgrids.");
            if (includeTimeline || structured?.IncludeTimeline == true)
                warnings.Add("Quick Create forms should not contain a Timeline tab.");
        }

        // Card form validation. Card forms (type 11) are tiny tile layouts —
        // single tab, minimal fields, no subgrids, no timeline. Dataverse won't
        // crash if these are violated but the result looks broken in the UCI.
        if (formType.Equals("card", StringComparison.OrdinalIgnoreCase))
        {
            var totalFields = structured is null
                ? tab1FieldList.Count + tab2FieldList.Count + headerFieldList.Count
                : structured.Tabs.SelectMany(t => t.Columns).SelectMany(c => c.Sections).SelectMany(s => s.Cells).Count(c => !string.Equals(c.ControlType, "subgrid", StringComparison.OrdinalIgnoreCase));
            var subgridCount = structured is null
                ? tab3SubgridList.Count
                : structured.Tabs.SelectMany(t => t.Columns).SelectMany(c => c.Sections).SelectMany(s => s.Cells).Count(c => string.Equals(c.ControlType, "subgrid", StringComparison.OrdinalIgnoreCase));
            var tabCount = structured?.Tabs.Count ?? (tab1FieldList.Count > 0 ? 1 : 0) + (tab2FieldList.Count > 0 ? 1 : 0);
            if (totalFields > 5)
                warnings.Add($"Card forms should have max 4-5 fields. Current count: {totalFields}.");
            if (subgridCount > 0)
                warnings.Add("Card forms should not contain subgrids.");
            if (includeTimeline || structured?.IncludeTimeline == true)
                warnings.Add("Card forms should not contain a Timeline tab.");
            if (tabCount > 1)
                warnings.Add($"Card forms should be single-tab. Current count: {tabCount}.");
        }

        // Header validation
        if (headerFieldList.Count > 4)
            warnings.Add($"Form header should have max 4 fields. Current count: {headerFieldList.Count}.");

        // Build FormXML — lowercase the table name too (Dataverse rejects mixed-case in objecttypecode)
        var lowerTable = tableLogicalName.ToLowerInvariant();
        var formXml = structured is not null
            ? BuildStructuredFormXml(lowerTable, formName, formType, structured, headerFieldList, fieldDisplayNames)
            : BuildFormXml(lowerTable, formName, formType, tab1FieldList,
                tab2FieldList, tab3SubgridList, includeTimeline, headerFieldList, fieldDisplayNames);

        // Build OData payload
        var payload = new Dictionary<string, object>
        {
            ["objecttypecode"] = lowerTable,
            ["name"] = formName,
            ["description"] = description,
            ["type"] = formTypeCode,
            ["formxml"] = formXml
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var riskLevel = formType.Equals("main", StringComparison.OrdinalIgnoreCase) ? "orange" : "green";

        // planId-by-reference. Past failure: agent passed the literal multi-KB
        // payload string (with embedded FormXML) back to ExecuteCreateForm, mangled
        // it during JSON serialization, and Dataverse rejected with HTTP 400
        // "Stream was not readable". Storing the payload server-side under a planId
        // means the executor looks it up — the agent never touches the JSON again.
        var planId = $"create_form_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new FormCreationPlan(
            PlanId: planId,
            TableLogicalName: lowerTable,
            FormName: formName,
            FormType: formType,
            Description: description,
            FormXml: formXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: riskLevel);
    }

    /// <summary>
    /// Splits a comma-separated field list AND lowercases each entry. Dataverse
    /// logical names are lowercase; agents have repeatedly passed PascalCase
    /// like "eppc_GiftName" which Dataverse rejects in formxml's datafieldname.
    /// </summary>
    private static List<string> NormalizeFieldList(string fields) =>
        string.IsNullOrWhiteSpace(fields)
            ? []
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToList();

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
    private static string BuildFormXml(
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

    // ───────────────────────────────────────────────────────────────────
    // Structured layout (tabsJson) — phase-1 of "Improve form management"
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse the agent-provided tabsJson into a <see cref="FormStructureSpec"/>.
    /// Case-insensitive property matching so the agent's natural camelCase or
    /// PascalCase input both work. Throws JsonException on malformed input;
    /// caller surfaces that as a REJECTED plan.
    /// Internal so PlanUpdateFormTool can reuse the same parser.
    /// </summary>
    internal static FormStructureSpec ParseStructured(string tabsJson, bool legacyTimeline, string legacyHeaderCsv)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Accept either { tabs: [...], headerFields: [...], includeTimeline: bool }
        // OR a bare array of tabs (the simplest shape) → wrap.
        using var doc = JsonDocument.Parse(tabsJson);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var bareTabs = JsonSerializer.Deserialize<List<FormTabSpec>>(root.GetRawText(), opts) ?? [];
            return new FormStructureSpec(
                Tabs: bareTabs,
                HeaderFields: NormalizeFieldList(legacyHeaderCsv),
                IncludeTimeline: legacyTimeline);
        }

        var spec = JsonSerializer.Deserialize<FormStructureSpec>(tabsJson, opts)
            ?? throw new JsonException("tabsJson deserialized to null");

        // The legacy `includeTimeline`/`headerFields` parameters still apply when
        // the structured spec is silent on them — preserves the agent's option to
        // mix the two conventions.
        return spec with
        {
            IncludeTimeline = spec.IncludeTimeline || legacyTimeline,
            HeaderFields = spec.HeaderFields is { Count: > 0 } ? spec.HeaderFields : NormalizeFieldList(legacyHeaderCsv),
        };
    }

    /// <summary>
    /// Schema-derived structural validation. Field-level "does this column
    /// exist on the table?" runs separately against the live metadata; this
    /// only catches shape errors that don't need a Dataverse round-trip.
    /// Internal so PlanUpdateFormTool can reuse the same validator.
    /// </summary>
    internal static List<string> ValidateStructured(FormStructureSpec spec)
    {
        var errors = new List<string>();
        if (spec.Tabs is null || spec.Tabs.Count == 0)
        {
            errors.Add("tabs array is empty — at least one tab is required.");
            return errors;
        }
        if (spec.Tabs.Count > 100)
            errors.Add($"tabs count {spec.Tabs.Count} exceeds the FormXML limit of 100.");

        for (var ti = 0; ti < spec.Tabs.Count; ti++)
        {
            var tab = spec.Tabs[ti];
            var tabRef = $"tabs[{ti}]";
            if (string.IsNullOrWhiteSpace(tab.Name)) errors.Add($"{tabRef}.name is required.");
            if (tab.Columns is null || tab.Columns.Count == 0)
            {
                errors.Add($"{tabRef}.columns is empty — at least one column is required.");
                continue;
            }
            if (tab.Columns.Count > 3)
                errors.Add($"{tabRef}.columns count {tab.Columns.Count} exceeds the FormXML limit of 3.");

            // Column widths must each match `^(100|[0-9]{1,2})%$` and sum to 100.
            var totalWidth = 0;
            for (var ci = 0; ci < tab.Columns.Count; ci++)
            {
                var col = tab.Columns[ci];
                var colRef = $"{tabRef}.columns[{ci}]";
                var width = col.Width?.Trim() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(width, @"^(100|[0-9]{1,2})%$"))
                {
                    errors.Add($"{colRef}.width '{col.Width}' is invalid; must be a percentage like '50%' or '100%'.");
                    continue;
                }
                if (int.TryParse(width.TrimEnd('%'), out var pct)) totalWidth += pct;

                // Validate subgrid params on every cell — phase 2.
                for (var si = 0; si < (col.Sections?.Count ?? 0); si++)
                {
                    var section = col.Sections![si];
                    var sectionRef = $"{colRef}.sections[{si}]";
                    for (var ki = 0; ki < (section.Cells?.Count ?? 0); ki++)
                    {
                        var cell = section.Cells![ki];
                        var cellRef = $"{sectionRef}.cells[{ki}]";
                        ValidateCell(cell, cellRef, errors);
                    }
                }
            }
            if (totalWidth != 100)
                errors.Add($"{tabRef} column widths sum to {totalWidth}%, expected 100%.");
        }

        // ─── Phase 5: form libraries + events ───
        var libNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (spec.Libraries is { Count: > 0 } libs)
        {
            for (var i = 0; i < libs.Count; i++)
            {
                var lib = libs[i];
                var libRef = $"libraries[{i}]";
                if (string.IsNullOrWhiteSpace(lib.Name))
                    errors.Add($"{libRef}.name is required (the JS web resource's logical name).");
                if (string.IsNullOrWhiteSpace(lib.UniqueId) || !Guid.TryParse(lib.UniqueId, out _))
                    errors.Add($"{libRef}.uniqueId '{lib.UniqueId}' must be a valid GUID (the web resource's record ID).");
                if (!string.IsNullOrWhiteSpace(lib.Name)) libNames.Add(lib.Name);
            }
        }

        if (spec.Events is { Count: > 0 } events)
        {
            var validEventTypes = new[] { "DataEvent", "ControlEvent" };
            var validNames = new[] { "onload", "onsave", "onchange" };
            for (var i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                var evRef = $"events[{i}]";
                if (!validEventTypes.Contains(ev.EventType, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{evRef}.eventType '{ev.EventType}' is invalid; must be 'DataEvent' or 'ControlEvent'.");
                if (!validNames.Contains((ev.Name ?? "").ToLowerInvariant()))
                    errors.Add($"{evRef}.name '{ev.Name}' is invalid; must be 'onload', 'onsave', or 'onchange'.");
                if (string.IsNullOrWhiteSpace(ev.FunctionName))
                    errors.Add($"{evRef}.functionName is required.");
                if (string.IsNullOrWhiteSpace(ev.LibraryName))
                    errors.Add($"{evRef}.libraryName is required.");
                else if (libNames.Count > 0 && !libNames.Contains(ev.LibraryName))
                    errors.Add($"{evRef}.libraryName '{ev.LibraryName}' is not in the registered libraries list. Add it to spec.libraries first.");

                // ControlEvent must specify which control. DataEvent must NOT.
                if (string.Equals(ev.EventType, "ControlEvent", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(ev.Attribute) && string.IsNullOrWhiteSpace(ev.Control))
                        errors.Add($"{evRef} is a ControlEvent but neither 'attribute' nor 'control' is set — Dataverse can't bind it.");
                }
            }
        }

        // ─── Phase 6: display conditions + ancestor ───
        if (spec.DisplayConditions is { } dc)
        {
            // Schema choice: Everyone OR Role[] — never both, never neither.
            var hasRoles = dc.RoleIds is { Count: > 0 };
            if (dc.Everyone && hasRoles)
                errors.Add("displayConditions cannot set both Everyone=true and RoleIds — pick one.");
            if (!dc.Everyone && !hasRoles)
                errors.Add("displayConditions requires either Everyone=true or at least one RoleId.");

            if (hasRoles)
            {
                for (var i = 0; i < dc.RoleIds!.Count; i++)
                {
                    if (!Guid.TryParse(dc.RoleIds[i], out _))
                        errors.Add($"displayConditions.roleIds[{i}] '{dc.RoleIds[i]}' is not a valid GUID.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.AncestorFormId))
        {
            if (!Guid.TryParse(spec.AncestorFormId, out var ancestorGuid))
            {
                errors.Add($"ancestorFormId '{spec.AncestorFormId}' is not a valid GUID.");
            }
            else if (ancestorGuid == Guid.Empty)
            {
                errors.Add("ancestorFormId is the all-zero GUID — Dataverse rejects it with \"Expected non-empty Guid\". Omit ancestorFormId, or pass a real systemform GUID from get_forms_list.");
            }
        }

        // Validate role GUIDs in DisplayConditions — same Guid.Empty bear-trap.
        if (spec.DisplayConditions?.RoleIds is { Count: > 0 } roleIds)
        {
            for (var i = 0; i < roleIds.Count; i++)
            {
                if (!Guid.TryParse(roleIds[i], out var rg))
                {
                    errors.Add($"displayConditions.roleIds[{i}] '{roleIds[i]}' is not a valid GUID.");
                }
                else if (rg == Guid.Empty)
                {
                    errors.Add($"displayConditions.roleIds[{i}] is the all-zero GUID — Dataverse rejects it. Pass real role GUIDs from get_security_roles, or use Everyone=true instead.");
                }
            }
        }

        // Validate library uniqueIds — these are the JS web resource ids referenced
        // by form events. An empty / zero one yields the same Dataverse error.
        if (spec.Libraries is { Count: > 0 } libraries)
        {
            for (var i = 0; i < libraries.Count; i++)
            {
                var libUid = libraries[i].UniqueId;
                if (string.IsNullOrWhiteSpace(libUid))
                {
                    errors.Add($"libraries[{i}].uniqueId is required (the web resource GUID this library refers to).");
                }
                else if (!Guid.TryParse(libUid, out var lg))
                {
                    errors.Add($"libraries[{i}].uniqueId '{libUid}' is not a valid GUID.");
                }
                else if (lg == Guid.Empty)
                {
                    errors.Add($"libraries[{i}].uniqueId is the all-zero GUID — Dataverse rejects it. Pass a real web-resource GUID from the maker portal.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Per-cell validation. Phase 2: subgrid params (AutoExpand enum, etc.).
    /// Phase 3: control-type rules (Format enum, SizeType enum, required
    /// WebResourceId / QuickForms, valid GUIDs on lookup view fields).
    /// </summary>
    private static void ValidateCell(FormCellSpec cell, string cellRef, List<string> errors)
    {
        var controlType = (cell.ControlType ?? "default").ToLowerInvariant();

        // Validate the control type itself first — silently misspelled types
        // would otherwise fall through to "default" and ship a broken form.
        var supportedTypes = new[] { "default", "owner", "lookup", "subgrid", "webresource", "quickform" };
        if (!supportedTypes.Contains(controlType))
        {
            errors.Add($"{cellRef}.controlType '{cell.ControlType}' is invalid; must be one of: {string.Join(", ", supportedTypes)}.");
            return;
        }

        // ─── Subgrid (phase 2) ─────────────────────────────────────────
        if (controlType == "subgrid")
        {
            if (string.IsNullOrWhiteSpace(cell.TargetEntity))
                errors.Add($"{cellRef}.targetEntity is required for subgrid cells.");

            if (!string.IsNullOrWhiteSpace(cell.AutoExpand))
            {
                var allowed = new[] { "Auto", "Fixed", "AutoWithFixedMax" };
                if (!allowed.Contains(cell.AutoExpand, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.autoExpand '{cell.AutoExpand}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }

            if (cell.RecordsPerPage is int rpp && (rpp < 1 || rpp > 250))
                errors.Add($"{cellRef}.recordsPerPage {rpp} is out of range (1-250).");

            if (cell.MaxRowsBeforeScroll is int mrbs && mrbs < 1)
                errors.Add($"{cellRef}.maxRowsBeforeScroll {mrbs} must be >= 1.");

            if (!string.IsNullOrWhiteSpace(cell.ChartGridMode))
            {
                var allowed = new[] { "ChartOnly", "GridOnly", "ChartAndGrid" };
                if (!allowed.Contains(cell.ChartGridMode, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.chartGridMode '{cell.ChartGridMode}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }

            if (!string.IsNullOrWhiteSpace(cell.VisualizationId))
            {
                if (!Guid.TryParse(cell.VisualizationId, out var visGuid))
                {
                    errors.Add($"{cellRef}.visualizationId '{cell.VisualizationId}' is not a valid GUID.");
                }
                else if (visGuid == Guid.Empty)
                {
                    // Dataverse rejects all-zero Guids with "Expected non-empty Guid"
                    // at create time — surface that as a plan error rather than letting
                    // the HTTP 400 leak back to the user. Past failure (real session):
                    // agent embedded "00000000-0000-0000-0000-000000000000" because it
                    // didn't have a real chart GUID; CreateForm returned HTTP 400.
                    errors.Add($"{cellRef}.visualizationId is the all-zero GUID — Dataverse rejects it with \"Expected non-empty Guid\". Either omit visualizationId / chartGridMode entirely, or call get_charts_list to fetch a real savedqueryvisualizationid for the subgrid's target entity.");
                }
            }

            // Coherence: setting a chart-related param without ChartGridMode is
            // probably a mistake — the chart won't render.
            if ((!string.IsNullOrWhiteSpace(cell.VisualizationId) || cell.EnableChartPicker == true)
                && string.IsNullOrWhiteSpace(cell.ChartGridMode))
            {
                errors.Add($"{cellRef} sets a chart param (visualizationId / enableChartPicker) but chartGridMode is missing — the chart won't render. Set chartGridMode to 'ChartAndGrid' or 'ChartOnly'.");
            }
            return;
        }

        // ─── WebResource (phase 3) ─────────────────────────────────────
        if (controlType == "webresource")
        {
            if (string.IsNullOrWhiteSpace(cell.Params?.WebResourceId))
                errors.Add($"{cellRef}.params.webResourceId is required for webresource cells.");

            if (!string.IsNullOrWhiteSpace(cell.Params?.SizeType))
            {
                var allowed = new[] { "StretchToFit", "StretchMaintainAspectRatio", "Original", "Specific" };
                if (!allowed.Contains(cell.Params!.SizeType, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.params.sizeType '{cell.Params.SizeType}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }
            if (!string.IsNullOrWhiteSpace(cell.Params?.HorizontalAlignment))
            {
                var allowed = new[] { "Left", "Right", "Center", "NotSet" };
                if (!allowed.Contains(cell.Params!.HorizontalAlignment, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.params.horizontalAlignment '{cell.Params.HorizontalAlignment}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }
            if (!string.IsNullOrWhiteSpace(cell.Params?.VerticalAlignment))
            {
                var allowed = new[] { "Top", "Middle", "Bottom", "NotSet" };
                if (!allowed.Contains(cell.Params!.VerticalAlignment, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.params.verticalAlignment '{cell.Params.VerticalAlignment}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }
            if (cell.Params?.Height is int h && h < 1)
                errors.Add($"{cellRef}.params.height {h} must be >= 1.");
            if (cell.Params?.Width is int w && w < 1)
                errors.Add($"{cellRef}.params.width {w} must be >= 1.");
            return;
        }

        // ─── QuickForm (phase 3) ───────────────────────────────────────
        if (controlType == "quickform")
        {
            if (string.IsNullOrWhiteSpace(cell.Params?.QuickForms))
                errors.Add($"{cellRef}.params.quickForms is required for quickform cells (one or more form GUIDs, comma- or semicolon-separated).");
            return;
        }

        // ─── default / lookup / owner — typed parameter validation ────
        if (cell.Params is null) return;

        if (!string.IsNullOrWhiteSpace(cell.Params.Format))
        {
            var allowed = new[]
            {
                "SingleLineOfText", "TextArea", "Email", "Url", "Ticker",
                "Date", "DateTime", "DateAndTime",
                "WholeNumber", "DecimalNumber", "Currency"
            };
            if (!allowed.Contains(cell.Params.Format, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{cellRef}.params.format '{cell.Params.Format}' is invalid; must be one of: {string.Join(", ", allowed)}.");
        }

        if (cell.Params.MaxLength is int ml && ml < 1)
            errors.Add($"{cellRef}.params.maxLength {ml} must be >= 1.");
        if (cell.Params.Precision is int p && (p < 0 || p > 10))
            errors.Add($"{cellRef}.params.precision {p} is out of range (0-10).");
        if (cell.Params.MinValue is double minV && cell.Params.MaxValue is double maxV && minV > maxV)
            errors.Add($"{cellRef}.params.minValue {minV} > maxValue {maxV}.");

        if (!string.IsNullOrWhiteSpace(cell.Params.DefaultViewId)
            && !Guid.TryParse(cell.Params.DefaultViewId, out _))
            errors.Add($"{cellRef}.params.defaultViewId '{cell.Params.DefaultViewId}' is not a valid GUID.");
    }

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
    private static string BuildStructuredFormXml(
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
