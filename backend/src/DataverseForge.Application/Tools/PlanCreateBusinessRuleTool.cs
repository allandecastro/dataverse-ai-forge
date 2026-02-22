using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateBusinessRuleTool
{
    [Description("Generate a plan for creating a Dataverse business rule. Returns the rule definition, "
        + "OData JSON payload, warnings, and risk level. Does NOT create the rule — use execute_create_business_rule for that.")]
    public Task<BusinessRuleCreationPlan> PlanCreateBusinessRuleAsync(
        [Description("The logical name of the table")] string tableLogicalName,
        [Description("The rule name (e.g., 'Set Priority Required When Status Active')")] string ruleName,
        [Description("Description of the rule")] string description,
        [Description("Scope: 'Entity' (all forms) or specific form name")] string scope,
        [Description("Conditions in format: 'field:operator:value:and' separated by semicolons (e.g., 'statecode:eq:0:and;cr123_type:eq:1:and')")] string conditionsDefinition,
        [Description("Actions in format: 'type:targetField:value' separated by semicolons (e.g., 'setRequired:cr123_priority:true;showError:cr123_priority:Priority is required')")] string actionsDefinition,
        [Description("Else actions in same format as actions, empty if not needed")] string elseActionsDefinition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        var conditions = ParseConditions(conditionsDefinition);
        var actions = ParseActions(actionsDefinition);
        var elseActions = ParseActions(elseActionsDefinition);

        if (conditions.Count == 0)
            warnings.Add("No conditions defined. The rule will always execute.");

        if (actions.Count == 0)
            warnings.Add("No actions defined. The rule will have no effect.");

        // Validate action types
        var validActionTypes = new[] { "setValue", "setRequired", "setVisibility", "showError", "setDefault", "lockField", "showSection", "hideTab" };
        foreach (var action in actions.Concat(elseActions))
        {
            if (!validActionTypes.Contains(action.Type))
                warnings.Add($"Action type '{action.Type}' is not standard. Expected one of: {string.Join(", ", validActionTypes)}.");
        }

        // Warning about client-side only
        warnings.Add("Business rules are CLIENT-SIDE only. They do NOT enforce rules via API/integrations. If server-side enforcement is needed, use a plugin instead.");

        // Build OData payload (workflows with category=2 for business rules)
        var payload = new Dictionary<string, object>
        {
            ["name"] = ruleName,
            ["primaryentity"] = tableLogicalName,
            ["description"] = description,
            ["category"] = 2, // Business Rule
            ["type"] = 1, // Definition
            ["scope"] = scope == "Entity" ? 1 : 2 // 1=Entity, 2=Form
        };

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new BusinessRuleCreationPlan(
            TableLogicalName: tableLogicalName,
            RuleName: ruleName,
            Description: description,
            Scope: scope,
            Conditions: conditions.AsReadOnly(),
            Actions: actions.AsReadOnly(),
            ElseActions: elseActions.AsReadOnly(),
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow"));
    }

    private static List<BusinessRuleCondition> ParseConditions(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return [];

        var conditions = new List<BusinessRuleCondition>();
        foreach (var entry in definition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 4);
            if (parts.Length >= 3)
            {
                conditions.Add(new BusinessRuleCondition(
                    parts[0].Trim(),
                    parts[1].Trim(),
                    parts[2].Trim(),
                    parts.Length >= 4 ? parts[3].Trim() : "and"));
            }
        }
        return conditions;
    }

    private static List<BusinessRuleAction> ParseActions(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return [];

        var actions = new List<BusinessRuleAction>();
        foreach (var entry in definition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 3);
            if (parts.Length >= 2)
            {
                actions.Add(new BusinessRuleAction(
                    parts[0].Trim(),
                    parts[1].Trim(),
                    parts.Length >= 3 ? parts[2].Trim() : null));
            }
        }
        return actions;
    }
}
