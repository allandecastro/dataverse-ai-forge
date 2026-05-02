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
        + "unsure of logical names.")]
    public async Task<FormCreationPlan> PlanCreateFormAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("The display name for the form (e.g., 'Work Order Main Form')")] string formName,
        [Description("The form type: 'main', 'quickCreate', or 'quickView'")] string formType,
        [Description("Description of the form")] string description,
        [Description("Comma-separated list of field LOGICAL names for tab 1. Use lowercase logical names (e.g., 'eppc_giftname,eppc_description'), NOT display names. System fields like 'statuscode' have no prefix.")] string tab1Fields,
        [Description("Comma-separated list of field logical names for tab 2 (Details), empty if not needed")] string tab2Fields,
        [Description("Comma-separated list of related entity logical names for subgrids in tab 3 (Related), empty if not needed")] string tab3Subgrids,
        [Description("Whether to include a Timeline tab for notes and activities")] bool includeTimeline,
        [Description("Comma-separated list of up to 4 field logical names for the header")] string headerFields,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Form type codes per the SystemForm.Type picklist (Dataverse SystemForm
        // entity reference). Past bug: "quickCreate" was mapped to 5, but 5 is
        // "Mobile - Express" — Quick Create is type 7.
        //   2 = Main, 6 = Quick View Form, 7 = Quick Create
        var formTypeCode = formType.ToLowerInvariant() switch
        {
            "main" => 2,
            "quickcreate" => 7,
            "quickview" => 6,
            _ => -1
        };

        if (formTypeCode == -1)
            warnings.Add($"Unrecognized form type '{formType}'. Expected 'main', 'quickCreate', or 'quickView'.");

        var tab1FieldList = NormalizeFieldList(tab1Fields);
        var tab2FieldList = NormalizeFieldList(tab2Fields);
        var tab3SubgridList = NormalizeFieldList(tab3Subgrids);
        var headerFieldList = NormalizeFieldList(headerFields);

        // Validate every referenced field actually exists on the table. The agent has
        // historically passed PascalCase / hallucinated names (e.g., eppc_ContactId for
        // a missing lookup, eppc_StatusCode instead of system 'statuscode'); Dataverse
        // then rejects the whole form payload with a 400 and the agent loops on retry.
        // Returning a rejection plan here makes the failure mode unambiguous.
        try
        {
            var details = await dataverseClient.GetTableDetailsAsync(tableLogicalName, cancellationToken);
            var available = new HashSet<string>(
                details.Columns.Select(c => c.LogicalName.ToLowerInvariant()),
                StringComparer.Ordinal);

            var allReferenced = tab1FieldList
                .Concat(tab2FieldList)
                .Concat(headerFieldList)
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

        // Quick create validation
        if (formType.Equals("quickCreate", StringComparison.OrdinalIgnoreCase))
        {
            var totalFields = tab1FieldList.Count + tab2FieldList.Count;
            if (totalFields > 7)
                warnings.Add($"Quick Create forms should have max 5-7 fields. Current count: {totalFields}.");
            if (tab3SubgridList.Count > 0)
                warnings.Add("Quick Create forms should not contain subgrids.");
            if (includeTimeline)
                warnings.Add("Quick Create forms should not contain a Timeline tab.");
        }

        // Header validation
        if (headerFieldList.Count > 4)
            warnings.Add($"Form header should have max 4 fields. Current count: {headerFieldList.Count}.");

        // Build FormXML — lowercase the table name too (Dataverse rejects mixed-case in objecttypecode)
        var lowerTable = tableLogicalName.ToLowerInvariant();
        var formXml = BuildFormXml(lowerTable, formName, formType, tab1FieldList,
            tab2FieldList, tab3SubgridList, includeTimeline, headerFieldList);

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
        List<string> headerFields)
        => BuildFormXml(tableLogicalName, formName, formType, tab1Fields,
            tab2Fields, tab3Subgrids, includeTimeline, headerFields);

    private static string BuildFormXml(
        string tableLogicalName,
        string formName,
        string formType,
        List<string> tab1Fields,
        List<string> tab2Fields,
        List<string> tab3Subgrids,
        bool includeTimeline,
        List<string> headerFields)
    {
        // Real GUIDs for tab/section/control IDs. Dataverse rejects formxml whose
        // `id` attributes contain placeholder strings like "{TAB_GENERAL}" — those
        // were never replaced and the resulting payload returns HTTP 400. Generate
        // a fresh GUID per-element each time the form is planned so two plans for
        // the same form don't collide.
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<form>");

        // Header. Every element in Dataverse FormXML carries its own GUID `id`.
        // FormXmlHeaderFooterType (the type behind <header> / <footer>) REQUIRES
        // an `id` attribute even though the published schema snippet doesn't show
        // it explicitly — Dataverse's runtime validator rejects the form with
        // "required attribute 'id' is missing" at line 0, position 0 if <header>
        // doesn't carry one. Same for <row> and <cell>.
        if (headerFields.Count > 0)
        {
            xml.AppendLine($"  <header id=\"{NewGuid()}\" columns=\"111\" showlabels=\"true\" showbar=\"false\">");
            xml.AppendLine("    <rows>");
            xml.AppendLine($"      <row id=\"{NewGuid()}\">");
            foreach (var field in headerFields.Take(4))
            {
                xml.AppendLine($"        <cell id=\"{NewGuid()}\"><control id=\"header_{field}\" classid=\"{{4273EDBD-AC1D-40d3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" /></cell>");
            }
            xml.AppendLine("      </row>");
            xml.AppendLine("    </rows>");
            xml.AppendLine("  </header>");
        }

        xml.AppendLine("  <tabs>");

        // Tab 1 - General/Summary
        if (tab1Fields.Count > 0)
        {
            var tabId = NewGuid();
            var sectionId = NewGuid();
            // Note: per the FormXML schema, `labelid` is FormGuidType (must be a GUID
            // if present). We use the <labels> child instead and omit labelid entirely
            // — Dataverse 400'd previously when we wrote labelid="General" (a string).
            xml.AppendLine($"    <tab name=\"tab_general\" id=\"{tabId}\" verticallayout=\"true\">");
            xml.AppendLine($"      <labels><label description=\"General\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine($"          <section name=\"sec_general\" id=\"{sectionId}\">");
            xml.AppendLine($"            <labels><label description=\"General\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows>");
            foreach (var field in tab1Fields)
            {
                xml.AppendLine($"              <row id=\"{NewGuid()}\"><cell id=\"{NewGuid()}\"><control id=\"{field}\" classid=\"{{4273EDBD-AC1D-40d3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" /></cell></row>");
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
            xml.AppendLine($"    <tab name=\"tab_details\" id=\"{tabId}\" verticallayout=\"true\">");
            xml.AppendLine($"      <labels><label description=\"Details\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine($"          <section name=\"sec_details\" id=\"{sectionId}\">");
            xml.AppendLine($"            <labels><label description=\"Details\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows>");
            foreach (var field in tab2Fields)
            {
                xml.AppendLine($"              <row id=\"{NewGuid()}\"><cell id=\"{NewGuid()}\"><control id=\"{field}\" classid=\"{{4273EDBD-AC1D-40d3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" /></cell></row>");
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
            xml.AppendLine($"    <tab name=\"tab_related\" id=\"{tabId}\" verticallayout=\"true\">");
            xml.AppendLine($"      <labels><label description=\"Related\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            foreach (var subgrid in tab3Subgrids)
            {
                var sectionId = NewGuid();
                var subgridUniqueId = NewGuid();
                xml.AppendLine($"          <section name=\"sec_{subgrid}\" id=\"{sectionId}\">");
                xml.AppendLine($"            <labels><label description=\"{subgrid}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                xml.AppendLine($"            <rows><row id=\"{NewGuid()}\"><cell id=\"{NewGuid()}\">");
                xml.AppendLine($"              <control id=\"subgrid_{subgrid}\" classid=\"{{E7A81278-8635-4D9E-8D4D-59480B391C5B}}\" uniqueid=\"{subgridUniqueId}\">");
                xml.AppendLine("                <parameters>");
                xml.AppendLine($"                  <TargetEntityType>{subgrid}</TargetEntityType>");
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

        // Tab 4 - Timeline
        if (includeTimeline && !formType.Equals("quickCreate", StringComparison.OrdinalIgnoreCase))
        {
            var tabId = NewGuid();
            var sectionId = NewGuid();
            var timelineUniqueId = NewGuid();
            xml.AppendLine($"    <tab name=\"tab_timeline\" id=\"{tabId}\" verticallayout=\"true\">");
            xml.AppendLine($"      <labels><label description=\"Timeline\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine($"          <section name=\"sec_timeline\" id=\"{sectionId}\">");
            xml.AppendLine($"            <labels><label description=\"Timeline\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine($"            <rows><row id=\"{NewGuid()}\"><cell id=\"{NewGuid()}\">");
            xml.AppendLine($"              <control id=\"notescontrol\" classid=\"{{B603CF9F-7028-4DB5-8DC2-4ED9ED6B26C4}}\" uniqueid=\"{timelineUniqueId}\" />");
            xml.AppendLine("            </cell></row></rows>");
            xml.AppendLine("          </section>");
            xml.AppendLine("        </sections>");
            xml.AppendLine("      </column></columns>");
            xml.AppendLine("    </tab>");
        }

        xml.AppendLine("  </tabs>");
        xml.AppendLine("</form>");

        return xml.ToString();
    }

    /// <summary>
    /// Format Dataverse expects in formxml: braced uppercase GUID like "{4273EDBD-AC1D-40d3-9FB2-095C621B552D}".
    /// </summary>
    private static string NewGuid() => Guid.NewGuid().ToString("B").ToUpperInvariant();
}
