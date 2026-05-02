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
    ISessionContext sessionContext)
{
    [Description("Generate a plan for UPDATING an existing Dataverse form. Returns a planId, "
        + "the new FormXML preview, warnings, and risk level. The full OData JSON payload is stored "
        + "server-side under planId. Pass the planId verbatim to execute_update_form after approval. "
        + "Validates that every field name actually exists on the table — call get_table_details first "
        + "if you're unsure of logical names. Use get_forms_list to find the formId of the form to update.")]
    public async Task<FormUpdatePlan> PlanUpdateFormAsync(
        [Description("The GUID of the existing form to update (from get_forms_list)")] string formId,
        [Description("Logical name of the table the form belongs to (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("New display name for the form. Pass the existing name if you don't want to rename.")] string formName,
        [Description("Form type: 'main', 'quickCreate', or 'quickView'")] string formType,
        [Description("Description for the form")] string description,
        [Description("Comma-separated list of field LOGICAL names for tab 1 (lowercase, e.g. 'eppc_giftname,eppc_contactid')")] string tab1Fields,
        [Description("Comma-separated list of field logical names for tab 2 (Details), empty if not needed")] string tab2Fields,
        [Description("Comma-separated list of related entity logical names for subgrids in tab 3 (Related), empty if not needed")] string tab3Subgrids,
        [Description("Whether to include a Timeline tab for notes and activities")] bool includeTimeline,
        [Description("Comma-separated list of up to 4 field logical names for the header. PASS ONLY FIELDS THE USER REQUESTED — do not invent header fields.")] string headerFields,
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
                RiskLevel: "red");
        }

        var warnings = new List<string>();
        var formTypeCode = formType.ToLowerInvariant() switch
        {
            "main" => 2,
            "quickcreate" => 5,
            "quickview" => 6,
            _ => -1
        };

        if (formTypeCode == -1)
            warnings.Add($"Unrecognized form type '{formType}'. Expected 'main', 'quickCreate', or 'quickView'.");

        var tab1FieldList = NormalizeFieldList(tab1Fields);
        var tab2FieldList = NormalizeFieldList(tab2Fields);
        var tab3SubgridList = NormalizeFieldList(tab3Subgrids);
        var headerFieldList = NormalizeFieldList(headerFields);

        // Validate fields exist on the table — same hard-reject pattern as PlanCreateForm.
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
                    RiskLevel: "red");
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
                RiskLevel: "red");
        }

        // Reuse the same FormXML builder as create. Both create and update post
        // the same shape; only the HTTP verb differs (POST vs PATCH on a
        // specific systemforms({id}) record).
        var lowerTable = tableLogicalName.ToLowerInvariant();
        var formXml = PlanCreateFormTool.BuildFormXmlPublic(
            lowerTable, formName, formType, tab1FieldList, tab2FieldList,
            tab3SubgridList, includeTimeline, headerFieldList);

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
            RiskLevel: "orange");
    }

    private static List<string> NormalizeFieldList(string fields) =>
        string.IsNullOrWhiteSpace(fields)
            ? []
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToList();
}
