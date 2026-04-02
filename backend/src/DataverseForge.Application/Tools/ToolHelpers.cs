namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared helpers for Dataverse OData payload construction.
/// </summary>
public static class ToolHelpers
{
    /// <summary>
    /// Default language code for Dataverse labels (English - US).
    /// </summary>
    public const int DefaultLanguageCode = 1033;

    /// <summary>
    /// Builds a Dataverse LocalizedLabel structure for the OData API.
    /// </summary>
    public static Dictionary<string, object> BuildLocalizedLabel(string label) =>
        new()
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.Label",
            ["LocalizedLabels"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["@odata.type"] = "Microsoft.Dynamics.CRM.LocalizedLabel",
                    ["Label"] = label,
                    ["LanguageCode"] = DefaultLanguageCode
                }
            }
        };

    /// <summary>
    /// Builds an XML label element for Dataverse form/dashboard XML.
    /// </summary>
    public static string BuildXmlLabel(string description) =>
        $"<labels><label description=\"{description}\" languagecode=\"{DefaultLanguageCode}\" /></labels>";
}
