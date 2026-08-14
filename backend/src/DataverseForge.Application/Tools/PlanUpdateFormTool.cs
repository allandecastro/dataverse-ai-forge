using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plan an update to an existing Dataverse form. Same FormXML generator as the
/// create tool, but targets an existing formId via PATCH systemforms({id}).
/// Stores the OData payload server-side under a planId so the agent can't
/// mangle it on the way to ExecuteUpdateForm.
/// </summary>
public class PlanUpdateFormTool(
    IDataverseClient dataverseClient,
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    XsdValidator xsdValidator)
{
    [Description("Generate a plan for UPDATING an existing Dataverse form. Returns a planId, "
        + "the new FormXML preview, warnings, and risk level. The full OData JSON payload is stored "
        + "server-side under planId. Pass the planId verbatim to execute_update_form after approval. "
        + "Validates that every field name actually exists on the table — call get_table_details first "
        + "if you're unsure of logical names. Use get_forms_list to find the formId of the form to update. "
        + "For multi-column / multi-section layouts pass tabsJson — when non-empty it overrides "
        + "the simple tab1Fields/tab2Fields/tab3Subgrids inputs.")]
    public async Task<FormUpdatePlan> PlanUpdateFormAsync(
        [Description("The GUID of the existing form to update (from get_forms_list)")] string formId,
        [Description("Logical name of the table the form belongs to (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("New display name for the form. Pass the existing name if you don't want to rename.")] string formName,
        [Description("Form type: 'main', 'quickCreate', 'quickView', or 'card'.")] string formType,
        [Description("Description for the form")] string description,
        [Description("Comma-separated list of field LOGICAL names for tab 1 (lowercase, e.g. 'eppc_giftname,eppc_contactid'). IGNORED when tabsJson is non-empty.")] string tab1Fields,
        [Description("Comma-separated list of field logical names for tab 2 (Details). IGNORED when tabsJson is non-empty.")] string tab2Fields,
        [Description("Comma-separated list of related entity logical names for subgrids in tab 3 (Related). IGNORED when tabsJson is non-empty.")] string tab3Subgrids,
        [Description("Whether to include a Timeline tab for notes and activities. Honored in both legacy and structured paths.")] bool includeTimeline,
        [Description("Comma-separated list of up to 4 field logical names for the header. IGNORED when tabsJson contains a 'headerFields' array.")] string headerFields,
        [Description("OPTIONAL structured layout (same shape as plan_create_form's tabsJson). When non-empty supersedes tab1/tab2/tab3 CSVs. Column widths must be percentages summing to 100 per tab; 1-3 columns per tab; 1-100 tabs.")] string tabsJson,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(formId, out var parsedFormId) || parsedFormId == Guid.Empty)
        {
            return new FormUpdatePlan(
                PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                FormId: Guid.Empty,
                TableLogicalName: tableLogicalName,
                FormName: formName,
                FormType: formType,
                Description: description,
                FormXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: new List<string> { "REJECTED: formId must be a valid GUID from get_forms_list." }.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        var warnings = new List<string>();
        // Form-type codes per the SystemForm.Type picklist:
        //   2 = Main, 6 = Quick View Form, 7 = Quick Create, 11 = Card.
        // Past bug: this used to map "quickcreate" => 5 (which is "Mobile - Express")
        // so any update to a quick-create form silently changed its type.
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

        // Branch on tabsJson. Mirrors PlanCreateFormTool — see that file for design notes.
        FormStructureSpec? structured = null;
        if (!string.IsNullOrWhiteSpace(tabsJson))
        {
            try
            {
                structured = FormStructureValidator.ParseStructured(tabsJson, includeTimeline, headerFields);
            }
            catch (System.Text.Json.JsonException jx)
            {
                return new FormUpdatePlan(
                    PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                    FormId: parsedFormId,
                    TableLogicalName: tableLogicalName,
                    FormName: formName,
                    FormType: formType,
                    Description: description,
                    FormXml: string.Empty,
                    WebApiPayloadJson: string.Empty,
                    Warnings: new List<string> { $"REJECTED: tabsJson is not valid JSON. {jx.Message}" }.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);
            }

            var structuredErrors = FormStructureValidator.ValidateStructured(structured);
            if (structuredErrors.Count > 0)
            {
                return new FormUpdatePlan(
                    PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                    FormId: parsedFormId,
                    TableLogicalName: tableLogicalName,
                    FormName: formName,
                    FormType: formType,
                    Description: description,
                    FormXml: string.Empty,
                    WebApiPayloadJson: string.Empty,
                    Warnings: new List<string> { "REJECTED: " + string.Join(" ", structuredErrors) }.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);
            }
        }

        var tab1FieldList = NormalizeFieldList(tab1Fields);
        var tab2FieldList = NormalizeFieldList(tab2Fields);
        var tab3SubgridList = NormalizeFieldList(tab3Subgrids);
        var headerFieldList = structured?.HeaderFields is { Count: > 0 } sh
            ? sh.Select(s => s.ToLowerInvariant()).ToList()
            : NormalizeFieldList(headerFields);

        // Validate fields exist on the table — same hard-reject pattern as PlanCreateForm.
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

            var structuredFields = structured is null
                ? Enumerable.Empty<string>()
                : structured.Tabs
                    .SelectMany(t => t.Columns)
                    .SelectMany(c => c.Sections)
                    .SelectMany(s => s.Cells)
                    .Where(c => !string.Equals(c.ControlType, "subgrid", StringComparison.OrdinalIgnoreCase))
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
                    + "Form update will fail at Dataverse with HTTP 400. "
                    + "Available logical names on this table (sample): " + sample + "."
                };
                return new FormUpdatePlan(
                    PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                    FormId: parsedFormId,
                    TableLogicalName: tableLogicalName,
                    FormName: formName,
                    FormType: formType,
                    Description: description,
                    FormXml: string.Empty,
                    WebApiPayloadJson: string.Empty,
                    Warnings: rejection.AsReadOnly(),
                    RiskLevel: PlanRisk.Red);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var rejection = new List<string>
            {
                $"REJECTED: Table '{tableLogicalName}' does not exist in this environment. "
                + $"Update will fail. Underlying lookup error: {ex.Message}"
            };
            return new FormUpdatePlan(
                PlanId: $"rejected_form_invalid_{Guid.NewGuid():N}",
                FormId: parsedFormId,
                TableLogicalName: tableLogicalName,
                FormName: formName,
                FormType: formType,
                Description: description,
                FormXml: string.Empty,
                WebApiPayloadJson: string.Empty,
                Warnings: rejection.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        // Reuse the same FormXML builder as create. Both create and update post
        // the same shape; only the HTTP verb differs (POST vs PATCH on a
        // specific systemforms({id}) record).
        var lowerTable = tableLogicalName.ToLowerInvariant();
        var formXml = structured is not null
            ? FormXmlBuilder.BuildStructuredFormXmlPublic(
                lowerTable, formName, formType, structured, headerFieldList, fieldDisplayNames)
            : FormXmlBuilder.BuildFormXmlPublic(
                lowerTable, formName, formType, tab1FieldList, tab2FieldList,
                tab3SubgridList, includeTimeline, headerFieldList, fieldDisplayNames);

        // XSD validation — same guard as PlanCreateFormTool. Catches structural
        // errors before sending to Dataverse, surfacing them with line/col context.
        var xsdResult = xsdValidator.Validate(formXml, "FormXml.xsd");
        if (!xsdResult.IsValid)
        {
            return new FormUpdatePlan(
                PlanId: $"rejected_form_xsd_{Guid.NewGuid():N}",
                FormId: parsedFormId,
                TableLogicalName: lowerTable,
                FormName: formName,
                FormType: formType,
                Description: description,
                FormXml: formXml,
                WebApiPayloadJson: string.Empty,
                Warnings: xsdResult.Errors.Prepend("REJECTED: FormXML failed XSD validation:").ToList().AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        // PATCH payload. Include `name`/`description`/`type` so renames work,
        // plus `formxml` for layout. `objecttypecode` should NOT change on
        // update — Dataverse rejects ownership type changes on existing records.
        var payload = new Dictionary<string, object>
        {
            ["name"] = formName,
            ["description"] = description,
            ["type"] = formTypeCode,
            ["formxml"] = formXml
        };
        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        var planId = $"update_form_{Guid.NewGuid():N}";
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new FormUpdatePlan(
            PlanId: planId,
            FormId: parsedFormId,
            TableLogicalName: lowerTable,
            FormName: formName,
            FormType: formType,
            Description: description,
            FormXml: formXml,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Orange);
    }

    private static List<string> NormalizeFieldList(string fields) =>
        string.IsNullOrWhiteSpace(fields)
            ? []
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToList();
}
