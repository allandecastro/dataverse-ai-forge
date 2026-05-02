using System.ComponentModel;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Check if a proposed table overlaps an existing Dataverse table the user already
/// has access to. Scores ALL tables (OOB + custom managed) in the environment by
/// token-overlap between the proposed name/description/fields and the existing
/// tables' logical/schema/display names.
///
/// No hardcoded catalog — the source of truth is what's actually in the user's
/// Dataverse environment. So if they have a Microsoft-shipped "Lead" table, an
/// Industry Cloud "Patient" table, or a managed solution's "Vehicle" table,
/// they all show up as candidates without us having to know about them up front.
/// </summary>
public class CheckOobTableCoverageTool(IDataverseClient dataverseClient)
{
    [Description("Check if a proposed custom table overlaps with existing Dataverse tables (OOB or "
        + "managed-solution tables already present in the environment). Returns the top-scoring matches "
        + "ranked by token overlap between the proposed table name/description/intended fields and the "
        + "existing tables' logical/schema/display names. Use this BEFORE plan_create_table when the user "
        + "describes a custom table — they may already have one that fits.")]
    public async Task<IReadOnlyList<OobTableRecommendation>> CheckOobTableCoverageAsync(
        [Description("The proposed table name (display name or logical name)")] string proposedTableName,
        [Description("Description of what the table will store (1-2 sentences)")] string proposedDescription,
        [Description("List of intended field names for the proposed table")] string[] intendedFields,
        CancellationToken cancellationToken)
    {
        var queryTokens = Tokenize(proposedTableName)
            .Concat(Tokenize(proposedDescription))
            .Concat(intendedFields.SelectMany(Tokenize))
            .Where(t => t.Length >= 3)
            .ToHashSet();

        if (queryTokens.Count == 0)
            return Array.Empty<OobTableRecommendation>();

        // Pull every table the user can see — managed (OOB / shipped solutions) + unmanaged.
        // We let the user's environment define what "covers" the proposed table; that catches
        // industry-cloud schemas (msdyn_*, msdyncrm_*, mscrmaddons_*, etc.) we'd never hardcode.
        var allTables = await dataverseClient.GetTablesListAsync(filter: "all", cancellationToken: cancellationToken);

        var recommendations = new List<OobTableRecommendation>();
        foreach (var table in allTables)
        {
            // Skip unmanaged custom tables from the user's own publishers — they're noise here.
            // The point of this tool is "do we already have something OOB-ish that fits?"
            if (table.IsCustom && !table.IsManaged)
                continue;

            var tableTokens = Tokenize(table.LogicalName)
                .Concat(Tokenize(table.SchemaName))
                .Concat(Tokenize(table.DisplayName))
                .Where(t => t.Length >= 3)
                .ToHashSet();

            if (tableTokens.Count == 0)
                continue;

            // Token-overlap score. We weight each match generously since the
            // table-side text is just the table identifier (logical/schema/display) —
            // no description in our current TableInfo — so even one strong overlap
            // (e.g. user says "account" and table is "account") is meaningful.
            var overlap = queryTokens.Intersect(tableTokens).ToList();
            if (overlap.Count == 0)
                continue;

            // 25 points per overlapping token. One overlap clears the noise floor;
            // multi-token overlaps quickly look high-confidence.
            var confidence = overlap.Count * 25;

            var reasons = new List<string>();

            // Boost: exact logical/schema/display match on the proposed table name.
            var proposedLower = proposedTableName.ToLowerInvariant();
            if (string.Equals(table.LogicalName, proposedLower, StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.SchemaName, proposedTableName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(table.DisplayName, proposedTableName, StringComparison.OrdinalIgnoreCase))
            {
                confidence += 40;
                reasons.Add($"Exact name match with '{table.LogicalName}'");
            }

            // Boost: managed and not custom = Microsoft OOB → strong "consider reusing".
            if (table.IsManaged && !table.IsCustom)
            {
                confidence += 10;
            }

            reasons.Add($"Token overlap: {string.Join(", ", overlap.Take(6))}");

            confidence = Math.Min(confidence, 100);
            if (confidence < 20) continue; // noise floor

            // What the proposed fields would NOT find in this candidate's name tokens.
            // We don't pull every column for every candidate (perf nightmare on 1700+ tables);
            // this is just a hint based on field names that didn't appear anywhere in the
            // candidate's textual identifiers.
            var missingFields = intendedFields
                .Where(f => !tableTokens.Contains(f.ToLowerInvariant()))
                .ToList()
                .AsReadOnly();

            recommendations.Add(new OobTableRecommendation(
                OobTableName: table.LogicalName,
                MatchConfidence: confidence,
                Explanation: string.Join("; ", reasons),
                MissingFields: missingFields,
                CoverageSummary: $"'{table.DisplayName}' ({table.LogicalName}) — managed: {table.IsManaged}, custom: {table.IsCustom}. "
                    + $"Confidence {confidence}% based on name/description token overlap. "
                    + "If this looks like a fit, call get_table_details to see actual columns before deciding."));
        }

        return recommendations
            .OrderByDescending(r => r.MatchConfidence)
            .Take(10)  // top-N — agent doesn't need a wall of weak matches
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Split a name or sentence into lowercased word tokens. Handles snake_case,
    /// camelCase, and PascalCase splits and drops short stopwords. Conservative —
    /// we'd rather miss a weak match than match on "the"/"of"/"a".
    /// </summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();

        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c))
            {
                // CamelCase boundary: lowercase→Uppercase splits a token.
                if (current.Length > 0 && char.IsUpper(c) && char.IsLower(text[i - 1]))
                {
                    AddToken(tokens, current);
                }
                current.Append(char.ToLowerInvariant(c));
            }
            else
            {
                AddToken(tokens, current);
            }
        }
        AddToken(tokens, current);

        return tokens;
    }

    private static void AddToken(List<string> tokens, System.Text.StringBuilder buf)
    {
        if (buf.Length == 0) return;
        var token = buf.ToString();
        buf.Clear();
        if (Stopwords.Contains(token)) return;
        tokens.Add(token);
    }

    private static readonly HashSet<string> Stopwords =
    [
        "the", "and", "for", "with", "that", "this", "from", "into", "have",
        "has", "are", "was", "were", "will", "can", "all", "any", "not",
        "but", "you", "your", "our", "their", "table", "field", "fields",
        "column", "columns", "data", "store", "stores", "name", "names"
    ];
}
