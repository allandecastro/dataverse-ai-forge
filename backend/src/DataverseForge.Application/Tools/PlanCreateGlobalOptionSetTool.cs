using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateGlobalOptionSetTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    // The agent serializes options with lowercase keys (per the [Description]
    // example "{value:..., label:..., color:...}"). System.Text.Json is
    // case-sensitive by default — without this, every option deserializes
    // with default values (Value=0, Label="") and the resulting Dataverse
    // payload has empty-labeled options. Past regression: a Choice field
    // creation came back with three "0/empty" options and the agent's
    // recovery attempt tripped Azure OpenAI's content filter.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Description("Generate a plan for creating a global option set in Dataverse. "
        + "Returns a planId, validation warnings, and risk level. The full OData JSON payload "
        + "is stored server-side under planId so it cannot be mangled. Pass the planId verbatim "
        + "to execute_create_global_option_set after approval. "
        + "Each option in optionsJson MAY include an optional 'color' (hex like '#1F77B4') "
        + "which Dataverse renders as a colored badge in views and forms.")]
    public async Task<GlobalOptionSetCreationPlan> PlanCreateGlobalOptionSetAsync(
        [Description("Schema name for the global option set (e.g., 'cr123_priority')")] string name,
        [Description("Display name for the global option set (e.g., 'Priority')")] string displayName,
        [Description("Description of the option set")] string description,
        [Description("Type of option set: 'Picklist' or 'Boolean'")] string optionSetType,
        [Description("JSON array of options. Each option: 'value' (int), 'label' (string), and optional 'color' "
            + "(hex string '#RRGGBB'). Example: [{\"value\":100000,\"label\":\"Low\",\"color\":\"#2CA02C\"},{\"value\":100001,\"label\":\"High\",\"color\":\"#D62728\"}]")] string optionsJson,
        [Description("Publisher customization prefix (e.g., 'cr123')")] string publisherPrefix,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var options = JsonSerializer.Deserialize<List<OptionInput>>(optionsJson, JsonOpts) ?? [];

        if (options.Count == 0)
            warnings.Add("Option set has no options defined. At least one option is recommended.");

        if (options.Count > 200)
            warnings.Add("Option sets with more than 200 options may impact form load performance.");

        if (!name.StartsWith(publisherPrefix + "_"))
            warnings.Add($"Option set name '{name}' should start with publisher prefix '{publisherPrefix}_'.");

        // Validate colors look like #RRGGBB if provided.
        foreach (var o in options)
        {
            if (!string.IsNullOrWhiteSpace(o.Color) && !ToolHelpers.LooksLikeHexColor(o.Color))
                warnings.Add($"Option '{o.Label}' has color '{o.Color}' that doesn't look like #RRGGBB — Dataverse may reject it.");
        }

        var optionEntries = options.Select(o => new OptionValueEntry(o.Value, o.Label, o.Color)).ToList();

        // Build OData JSON payload
        // Each option needs its own @odata.type tag so the OptionMetadata
        // collection deserializes via polymorphism. Without it Dataverse
        // returns 400 "unreadable payload" with no useful detail.
        var optionsList = options.Select(o =>
        {
            var entry = new Dictionary<string, object>
            {
                ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionMetadata",
                ["Value"] = o.Value,
                ["Label"] = ToolHelpers.BuildLocalizedLabel(o.Label),
            };
            if (!string.IsNullOrWhiteSpace(o.Color))
                entry["Color"] = o.Color!;
            return entry;
        }).ToArray();

        // Lowercase the schema name — Dataverse logical names are lowercase.
        // Agents have repeatedly passed PascalCase ("eppc_Type") which Dataverse
        // either rejects or silently corrupts. Past regression: every retry
        // failed with a 400 because the sent Name didn't match Dataverse's
        // expectation.
        var lowerName = name.ToLowerInvariant();
        if (!string.Equals(name, lowerName, StringComparison.Ordinal))
        {
            warnings.Add($"Name '{name}' was lowercased to '{lowerName}' — Dataverse logical names must be lowercase.");
        }

        // Pre-existence check. Past regression: agent planned a creation, the
        // option set already existed in the environment, ExecuteCreateGlobalOptionSet
        // failed at runtime, the plan stayed in PendingPlans, and the agent looped
        // trying to "discard" it. Reject the plan up-front so this never happens.
        var existingId = await dataverseClient.ResolveGlobalOptionSetMetadataIdAsync(lowerName, cancellationToken);
        if (existingId is not null)
        {
            var rejection = new List<string>
            {
                $"REJECTED: Global option set '{lowerName}' already exists in this environment "
                + $"(MetadataId: {existingId.Value}). Re-creation is not allowed and is unnecessary. "
                + "If you need to add it to a field, call plan_create_field with "
                + $"fieldType='globalOptionSet' and fieldTypeConfigJson='{{\"globalOptionSetName\":\"{lowerName}\"}}'. "
                + "If you need to add or update options on this set, use plan_add_option_to_option_set "
                + "or plan_update_option. DO NOT execute this plan."
            };
            return new GlobalOptionSetCreationPlan(
                PlanId: $"rejected_optionset_exists_{Guid.NewGuid():N}",
                Name: lowerName,
                DisplayName: displayName,
                Description: description,
                OptionSetType: optionSetType,
                Options: optionEntries.AsReadOnly(),
                WebApiPayloadJson: string.Empty,
                Warnings: rejection.AsReadOnly(),
                RiskLevel: PlanRisk.Red);
        }

        var payload = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.OptionSetMetadata",
            ["Name"] = lowerName,
            ["DisplayName"] = ToolHelpers.BuildLocalizedLabel(displayName),
            ["Description"] = ToolHelpers.BuildLocalizedLabel(description),
            ["IsGlobal"] = true,
            ["OptionSetType"] = optionSetType,
            ["Options"] = optionsList
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var planId = $"create_global_option_set_{Guid.NewGuid():N}";

        // Persist payload server-side; ExecuteCreateGlobalOptionSet looks it up by planId
        // so the agent can't mangle the OData JSON. Past failure: agent passed the literal
        // string "{...payload for global option set eppc_type...}" as webApiPayloadJson and
        // Dataverse returned a "Stream was not readable" 400.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new GlobalOptionSetCreationPlan(
            PlanId: planId,
            Name: lowerName,
            DisplayName: displayName,
            Description: description,
            OptionSetType: optionSetType,
            Options: optionEntries.AsReadOnly(),
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Yellow);
    }

    private sealed class OptionInput
    {
        public int Value { get; set; }
        public string Label { get; set; } = string.Empty;
        public string? Color { get; set; }
    }
}
