namespace DVForge.Domain.Models;

public record EnvironmentInfo(
    string OrganizationName,
    string EnvironmentUrl,
    int BaseLanguageLcid,
    string DataverseVersion,
    string Region,
    string BaseCurrencyCode,
    bool IsAuditEnabled);
