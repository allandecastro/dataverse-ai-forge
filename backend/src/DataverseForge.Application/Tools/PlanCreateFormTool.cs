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
                structured = FormStructureValidator.ParseStructured(tabsJson, includeTimeline, headerFields);
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
                    RiskLevel: PlanRisk.Red);
            }

            var structuredErrors = FormStructureValidator.ValidateStructured(structured);
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
                    RiskLevel: PlanRisk.Red);
            }
        }

        var tab1FieldList = FormStructureValidator.NormalizeFieldList(tab1Fields);
        var tab2FieldList = FormStructureValidator.NormalizeFieldList(tab2Fields);
        var tab3SubgridList = FormStructureValidator.NormalizeFieldList(tab3Subgrids);
        var headerFieldList = structured?.HeaderFields is { Count: > 0 } sh
            ? sh.Select(s => s.ToLowerInvariant()).ToList()
            : FormStructureValidator.NormalizeFieldList(headerFields);

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
                    RiskLevel: PlanRisk.Red);
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
                RiskLevel: PlanRisk.Red);
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
            ? FormXmlBuilder.BuildStructuredFormXml(lowerTable, formName, formType, structured, headerFieldList, fieldDisplayNames)
            : FormXmlBuilder.BuildFormXml(lowerTable, formName, formType, tab1FieldList,
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

        var riskLevel = formType.Equals("main", StringComparison.OrdinalIgnoreCase) ? PlanRisk.Orange : PlanRisk.Green;

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

}
