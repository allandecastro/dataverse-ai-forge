using System.Text.RegularExpressions;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared helpers for Dataverse OData payload construction.
/// </summary>
public static partial class ToolHelpers
{
    /// <summary>
    /// Default language code for Dataverse labels (English - US).
    /// </summary>
    public const int DefaultLanguageCode = 1033;

    /// <summary>
    /// Validates that a string looks like a Dataverse OptionMetadata.Color value
    /// (#RRGGBB hex format). Dataverse rejects other formats.
    /// </summary>
    public static bool LooksLikeHexColor(string? color)
        => !string.IsNullOrWhiteSpace(color) && HexColorRegex().IsMatch(color);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColorRegex();

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

    /// <summary>
    /// Returns true when two human-readable names look similar enough that the user
    /// probably meant the same thing. Compares the lowercase, alphanumeric-only forms.
    /// "Vehicle Make", "vehicle_make", and "VehicleMake" all collapse to "vehiclemake".
    /// </summary>
    public static bool LooksLikeSameName(string a, string b)
    {
        var na = NormalizeName(a);
        var nb = NormalizeName(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        // One contains the other when both are at least 4 chars — catches "VIN" vs "VehicleVIN"
        return na.Length >= 4 && nb.Length >= 4 && (na.Contains(nb) || nb.Contains(na));
    }

    private static string NormalizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sanitizes a display name into a valid Dataverse schema identifier.
    /// Dataverse logical/schema names allow only [A-Za-z0-9_], must start with a letter,
    /// and must be 1–64 chars (without prefix). This strips parens, hyphens, accents, and
    /// other punctuation that users naturally include in display names.
    /// </summary>
    public static string SanitizeSchemaName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return string.Empty;

        var sb = new System.Text.StringBuilder(displayName.Length);
        foreach (var ch in displayName)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            // spaces, parens, hyphens, slashes, accents, etc. → drop
        }
        var cleaned = sb.ToString();
        // Schema names must start with a letter
        if (cleaned.Length > 0 && !char.IsLetter(cleaned[0]))
            cleaned = "X" + cleaned;
        // Cap to a reasonable length so we leave room for the publisher prefix
        if (cleaned.Length > 50)
            cleaned = cleaned[..50];
        return cleaned;
    }
}
