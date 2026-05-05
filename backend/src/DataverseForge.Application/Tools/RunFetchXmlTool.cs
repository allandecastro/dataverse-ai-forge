using System.ComponentModel;
using System.Xml.Linq;
using DataverseForge.Domain.Interfaces;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Runs an arbitrary FetchXML query against a Dataverse entity set. Read-only.
/// Complements <c>get_records</c> (OData) for cases that OData can't express:
/// aggregates, link-entity joins, FetchXML-specific operators
/// (<c>eq-userid</c>, <c>this-fiscal-year</c>, <c>last-x-months</c>, <c>in</c>),
/// and queries that should mirror what views/charts already use.
/// </summary>
public class RunFetchXmlTool(IDataverseClient dataverseClient)
{
    [Description("Execute a FetchXML query against a Dataverse entity set. Read-only — returns the "
        + "matching rows. Use this when OData $filter isn't enough: aggregates (sum/count distinct), "
        + "link-entity joins (multi-entity queries), Dataverse-specific operators "
        + "(eq-userid, this-fiscal-year, last-x-months, in, not-in, etc.), or when you want the "
        + "same query shape views/charts use. For simple filters, prefer get_records (cheaper to author). "
        + "fetchXml MUST be a single <fetch>…</fetch> root element with no xmlns. "
        + "Worked example: <fetch top=\"5\"><entity name=\"contact\"><attribute name=\"contactid\"/>"
        + "<attribute name=\"fullname\"/><filter type=\"and\"><condition attribute=\"statecode\" "
        + "operator=\"eq\" value=\"0\"/></filter></entity></fetch>. "
        + "Pass entitySetName as the plural collection name (e.g. 'contacts', 'andras_gifts').")]
    public async Task<object> RunFetchXmlAsync(
        [Description("Entity set name (plural) — e.g. 'contacts', 'andras_gifts', 'opportunities'. "
            + "Must match the entity in the FetchXML root <entity name=\"…\"/>.")]
        string entitySetName,
        [Description("Single <fetch>…</fetch> root element, no xmlns. Validated server-side; "
            + "rejected if root isn't <fetch> or XML doesn't parse.")]
        string fetchXml,
        // Default makes top optional at the binder so calls without it bind
        // cleanly instead of failing with the opaque "Error: Function failed."
        [Description("Max rows to return (1-500, default 50). Caps the result regardless of any "
            + "<fetch count=\"…\"> embedded in the XML.")]
        int top = 50,
        CancellationToken cancellationToken = default)
    {
        if (top <= 0) top = 50;
        if (top > 500) top = 500;

        // Catch and surface real errors so the agent can quote the actual cause
        // instead of saying "function failed". Local validation errors land in
        // the same envelope so the agent doesn't have to special-case them.
        try
        {
            if (string.IsNullOrWhiteSpace(entitySetName))
                throw new ArgumentException("entitySetName is required.", nameof(entitySetName));
            if (string.IsNullOrWhiteSpace(fetchXml))
                throw new ArgumentException("fetchXml is required.", nameof(fetchXml));

            XElement parsed;
            try
            {
                parsed = XElement.Parse(fetchXml);
            }
            catch (System.Xml.XmlException xx)
            {
                throw new ArgumentException(
                    $"fetchXml is not valid XML: {xx.Message}", nameof(fetchXml), xx);
            }

            if (!string.Equals(parsed.Name.LocalName, "fetch", StringComparison.Ordinal))
                throw new ArgumentException(
                    $"fetchXml root element must be <fetch>, got <{parsed.Name.LocalName}>.",
                    nameof(fetchXml));

            if (parsed.Name.NamespaceName.Length > 0)
                throw new ArgumentException(
                    "fetchXml must be in the default namespace (no xmlns attribute on <fetch>).",
                    nameof(fetchXml));

            return await dataverseClient.RunFetchXmlAsync(entitySetName, fetchXml, top, cancellationToken);
        }
        catch (Exception ex)
        {
            return new
            {
                error = $"RunFetchXml failed for '{entitySetName}': {ex.Message}",
                entitySetName,
                top,
            };
        }
    }
}
