using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Schema;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Validates XML strings against bundled Dataverse XSD schemas (FormXml.xsd, Fetch.xsd,
/// SiteMap.xsd, VisualizationDataDescription.xsd). Schemas are loaded from
/// <c>$DVFORGE_SCHEMA_ROOT</c> if set, else <c>{AppContext.BaseDirectory}/schemas/9.0.0.2090</c>.
/// Loaded <see cref="XmlSchemaSet"/> instances are cached per-file so repeated
/// validations don't re-parse the XSDs (which are large and slow to load).
///
/// Registered as a singleton so the cache survives request scope. Thread-safe
/// via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class XsdValidator
{
    private const string DefaultSchemaSubdirectory = "schemas/9.0.0.2090";
    private const string SchemaRootEnvVar = "DVFORGE_SCHEMA_ROOT";

    private readonly ConcurrentDictionary<string, XmlSchemaSet> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Validate <paramref name="xml"/> against the named XSD (e.g. "SiteMap.xsd").
    /// Returns <c>(true, [])</c> on success or <c>(false, [errors...])</c> with
    /// each error annotated with line/column and severity.
    /// </summary>
    public XsdValidationResult Validate(string xml, string xsdFileName)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return new XsdValidationResult(false, new[] { "XML payload was empty." });

        XmlSchemaSet schemaSet;
        try
        {
            schemaSet = LoadOrGetSchemaSet(xsdFileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new XsdValidationResult(false, new[] { $"Could not load schema '{xsdFileName}': {ex.Message}" });
        }

        var errors = new List<string>();

        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = schemaSet,
            ValidationFlags = XmlSchemaValidationFlags.ProcessIdentityConstraints
                | XmlSchemaValidationFlags.ReportValidationWarnings,
        };
        settings.ValidationEventHandler += (_, e) =>
        {
            // Only hard errors count against IsValid. Warnings ("Could not find
            // schema information for element X") arise from intentionally loose
            // XSD regions (e.g. the FetchXML content inside <fetch> in
            // VisualizationDataDescription.xsd) and must not block the plan.
            if (e.Severity == XmlSeverityType.Error)
            {
                errors.Add($"Error (line {e.Exception.LineNumber}, col {e.Exception.LinePosition}): {e.Message}");
            }
        };

        try
        {
            using var sr = new StringReader(xml);
            using var xr = XmlReader.Create(sr, settings);
            while (xr.Read()) { /* drive validation */ }
        }
        catch (XmlException xex)
        {
            errors.Add($"XML is not well-formed (line {xex.LineNumber}, col {xex.LinePosition}): {xex.Message}");
        }

        return new XsdValidationResult(errors.Count == 0, errors);
    }

    private XmlSchemaSet LoadOrGetSchemaSet(string xsdFileName)
    {
        return _cache.GetOrAdd(xsdFileName, name =>
        {
            var schemaDir = ResolveSchemaDirectory();
            var path = Path.Combine(schemaDir, name);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"XSD '{name}' not found in '{schemaDir}'. Set the {SchemaRootEnvVar} env var to override the schema directory.",
                    path);
            }

            var set = new XmlSchemaSet
            {
                // Allow `<xs:include schemaLocation="SiteMapType.xsd"/>` to
                // resolve relative to the same directory as the parent XSD.
                XmlResolver = new XmlUrlResolver(),
            };
            set.Add(targetNamespace: null, schemaUri: path);
            set.Compile();
            return set;
        });
    }

    private static string ResolveSchemaDirectory()
    {
        var envOverride = Environment.GetEnvironmentVariable(SchemaRootEnvVar);
        if (!string.IsNullOrWhiteSpace(envOverride)) return envOverride;
        return Path.Combine(AppContext.BaseDirectory, DefaultSchemaSubdirectory);
    }
}

public sealed record XsdValidationResult(bool IsValid, IReadOnlyList<string> Errors);
