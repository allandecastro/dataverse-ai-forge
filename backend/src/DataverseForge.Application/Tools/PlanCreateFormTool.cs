using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateFormTool
{
    [Description("Generate a plan for creating a Dataverse form. Returns the FormXML, OData JSON payload, "
        + "warnings, and risk level. Does NOT create the form — use execute_create_form for that.")]
    public Task<FormCreationPlan> PlanCreateFormAsync(
        [Description("The logical name of the table (e.g., 'cr123_workorder')")] string tableLogicalName,
        [Description("The display name for the form (e.g., 'Work Order Main Form')")] string formName,
        [Description("The form type: 'main', 'quickCreate', or 'quickView'")] string formType,
        [Description("Description of the form")] string description,
        [Description("Comma-separated list of field logical names for tab 1 (General/Summary)")] string tab1Fields,
        [Description("Comma-separated list of field logical names for tab 2 (Details), empty if not needed")] string tab2Fields,
        [Description("Comma-separated list of related entity logical names for subgrids in tab 3 (Related), empty if not needed")] string tab3Subgrids,
        [Description("Whether to include a Timeline tab for notes and activities")] bool includeTimeline,
        [Description("Comma-separated list of up to 4 field logical names for the header")] string headerFields,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Validate form type
        var formTypeCode = formType.ToLowerInvariant() switch
        {
            "main" => 2,
            "quickcreate" => 5,
            "quickview" => 6,
            _ => -1
        };

        if (formTypeCode == -1)
            warnings.Add($"Unrecognized form type '{formType}'. Expected 'main', 'quickCreate', or 'quickView'.");

        var tab1FieldList = ParseFieldList(tab1Fields);
        var tab2FieldList = ParseFieldList(tab2Fields);
        var tab3SubgridList = ParseFieldList(tab3Subgrids);
        var headerFieldList = ParseFieldList(headerFields);

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

        // Build FormXML
        var formXml = BuildFormXml(tableLogicalName, formName, formType, tab1FieldList,
            tab2FieldList, tab3SubgridList, includeTimeline, headerFieldList);

        // Build OData payload
        var payload = new Dictionary<string, object>
        {
            ["objecttypecode"] = tableLogicalName,
            ["name"] = formName,
            ["description"] = description,
            ["type"] = formTypeCode,
            ["formxml"] = formXml
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var riskLevel = formType.Equals("main", StringComparison.OrdinalIgnoreCase) ? "orange" : "green";

        return Task.FromResult(new FormCreationPlan(
            TableLogicalName: tableLogicalName,
            FormName: formName,
            FormType: formType,
            Description: description,
            FormXml: formXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: riskLevel));
    }

    private static List<string> ParseFieldList(string fields) =>
        string.IsNullOrWhiteSpace(fields)
            ? []
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<form>");

        // Header
        if (headerFields.Count > 0)
        {
            xml.AppendLine("  <header>");
            xml.AppendLine("    <rows>");
            xml.AppendLine("      <row>");
            foreach (var field in headerFields.Take(4))
            {
                xml.AppendLine($"        <cell><control id=\"header_{field}\" classid=\"{{4273EDBD-AC1D-40d3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" /></cell>");
            }
            xml.AppendLine("      </row>");
            xml.AppendLine("    </rows>");
            xml.AppendLine("  </header>");
        }

        xml.AppendLine("  <tabs>");

        // Tab 1 - General/Summary
        if (tab1Fields.Count > 0)
        {
            xml.AppendLine("    <tab name=\"tab_general\" id=\"{TAB_GENERAL}\" labelid=\"General\">");
            xml.AppendLine($"      <labels><label description=\"General\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine("          <section name=\"sec_general\" id=\"{SEC_GENERAL}\">");
            xml.AppendLine($"            <labels><label description=\"General\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows>");
            foreach (var field in tab1Fields)
            {
                xml.AppendLine($"              <row><cell><control id=\"{field}\" classid=\"{{4273EDBD-AC1D-40d3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" /></cell></row>");
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
            xml.AppendLine("    <tab name=\"tab_details\" id=\"{TAB_DETAILS}\" labelid=\"Details\">");
            xml.AppendLine($"      <labels><label description=\"Details\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine("          <section name=\"sec_details\" id=\"{SEC_DETAILS}\">");
            xml.AppendLine($"            <labels><label description=\"Details\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows>");
            foreach (var field in tab2Fields)
            {
                xml.AppendLine($"              <row><cell><control id=\"{field}\" classid=\"{{4273EDBD-AC1D-40d3-9FB2-095C621B552D}}\" datafieldname=\"{field}\" /></cell></row>");
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
            xml.AppendLine("    <tab name=\"tab_related\" id=\"{TAB_RELATED}\" labelid=\"Related\">");
            xml.AppendLine($"      <labels><label description=\"Related\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            foreach (var subgrid in tab3Subgrids)
            {
                xml.AppendLine($"          <section name=\"sec_{subgrid}\" id=\"{{SEC_{subgrid.ToUpperInvariant()}}}\">");
                xml.AppendLine($"            <labels><label description=\"{subgrid}\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
                xml.AppendLine("            <rows><row><cell>");
                xml.AppendLine($"              <control id=\"subgrid_{subgrid}\" classid=\"{{E7A81278-8635-4D9E-8D4D-59480B391C5B}}\" uniqueid=\"{{SUBGRID_{subgrid.ToUpperInvariant()}}}\">");
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
            xml.AppendLine("    <tab name=\"tab_timeline\" id=\"{TAB_TIMELINE}\" labelid=\"Timeline\">");
            xml.AppendLine($"      <labels><label description=\"Timeline\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("      <columns><column width=\"100%\">");
            xml.AppendLine("        <sections>");
            xml.AppendLine("          <section name=\"sec_timeline\" id=\"{SEC_TIMELINE}\">");
            xml.AppendLine($"            <labels><label description=\"Timeline\" languagecode=\"{ToolHelpers.DefaultLanguageCode}\" /></labels>");
            xml.AppendLine("            <rows><row><cell>");
            xml.AppendLine("              <control id=\"notescontrol\" classid=\"{B603CF9F-7028-4DB5-8DC2-4ED9ED6B26C4}\" uniqueid=\"{TIMELINE_CONTROL}\" />");
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
}
