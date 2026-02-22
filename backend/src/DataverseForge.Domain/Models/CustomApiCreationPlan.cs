namespace DataverseForge.Domain.Models;

public record CustomApiCreationPlan(
    string UniqueName,
    string DisplayName,
    string Description,
    string BindingType,
    string? BoundEntityLogicalName,
    bool IsFunction,
    bool IsPrivate,
    IReadOnlyList<CustomApiParameter> RequestParameters,
    IReadOnlyList<CustomApiResponseProperty> ResponseProperties,
    string? PluginCode,
    string WebApiPayloadJson,
    IReadOnlyList<string> Warnings,
    string RiskLevel);

public record CustomApiParameter(
    string UniqueName,
    string DisplayName,
    string Type,
    bool IsOptional);

public record CustomApiResponseProperty(
    string UniqueName,
    string DisplayName,
    string Type);
