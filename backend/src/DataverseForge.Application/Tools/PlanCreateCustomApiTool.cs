using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class PlanCreateCustomApiTool
{
    [Description("Generate a plan for creating a Dataverse Custom API. Returns the API specification, "
        + "OData JSON payload, warnings, and risk level. Does NOT create the API — use execute_create_custom_api for that.")]
    public Task<CustomApiCreationPlan> PlanCreateCustomApiAsync(
        [Description("The unique name for the API (e.g., 'cr123_CalculateWorkOrderCost')")] string uniqueName,
        [Description("The display name")] string displayName,
        [Description("Description of the API")] string description,
        [Description("Binding type: 'Global', 'Entity', or 'EntityCollection'")] string bindingType,
        [Description("Bound entity logical name (required for Entity/EntityCollection binding, empty for Global)")] string? boundEntityLogicalName,
        [Description("Whether this is a function (GET, no side effects) or action (POST)")] bool isFunction,
        [Description("Whether the API is private (org-only) or public")] bool isPrivate,
        [Description("Request parameters in format: 'name:displayName:type:optional' separated by semicolons")] string requestParametersDefinition,
        [Description("Response properties in format: 'name:displayName:type' separated by semicolons")] string responsePropertiesDefinition,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Validate binding type
        if (bindingType is not ("Global" or "Entity" or "EntityCollection"))
            warnings.Add($"Binding type '{bindingType}' is not valid. Expected 'Global', 'Entity', or 'EntityCollection'.");

        if (bindingType != "Global" && string.IsNullOrWhiteSpace(boundEntityLogicalName))
            warnings.Add($"Binding type '{bindingType}' requires a bound entity logical name.");

        if (isFunction)
            warnings.Add("Functions (GET) should have no side effects. If the API modifies data, set isFunction=false.");

        var requestParams = ParseRequestParameters(requestParametersDefinition);
        var responseProps = ParseResponseProperties(responsePropertiesDefinition);

        // Build OData payload
        var payload = new Dictionary<string, object>
        {
            ["uniquename"] = uniqueName,
            ["name"] = displayName,
            ["displayname"] = displayName,
            ["description"] = description,
            ["bindingtype"] = bindingType switch
            {
                "Global" => 0,
                "Entity" => 1,
                "EntityCollection" => 2,
                _ => 0
            },
            ["isfunction"] = isFunction,
            ["isprivate"] = isPrivate,
            ["allowedcustomprocessingsteptype"] = 0 // None (no plugin required initially)
        };

        if (!string.IsNullOrWhiteSpace(boundEntityLogicalName))
            payload["boundentitylogicalname"] = boundEntityLogicalName;

        var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

        return Task.FromResult(new CustomApiCreationPlan(
            UniqueName: uniqueName,
            DisplayName: displayName,
            Description: description,
            BindingType: bindingType,
            BoundEntityLogicalName: boundEntityLogicalName,
            IsFunction: isFunction,
            IsPrivate: isPrivate,
            RequestParameters: requestParams.AsReadOnly(),
            ResponseProperties: responseProps.AsReadOnly(),
            PluginCode: null,
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "orange"));
    }

    private static List<CustomApiParameter> ParseRequestParameters(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return [];

        var parameters = new List<CustomApiParameter>();
        foreach (var entry in definition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 4);
            if (parts.Length >= 3)
            {
                parameters.Add(new CustomApiParameter(
                    parts[0].Trim(),
                    parts[1].Trim(),
                    parts[2].Trim(),
                    parts.Length >= 4 && parts[3].Trim().Equals("optional", StringComparison.OrdinalIgnoreCase)));
            }
        }
        return parameters;
    }

    private static List<CustomApiResponseProperty> ParseResponseProperties(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition))
            return [];

        var properties = new List<CustomApiResponseProperty>();
        foreach (var entry in definition.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 3);
            if (parts.Length >= 3)
            {
                properties.Add(new CustomApiResponseProperty(
                    parts[0].Trim(),
                    parts[1].Trim(),
                    parts[2].Trim()));
            }
        }
        return properties;
    }
}
