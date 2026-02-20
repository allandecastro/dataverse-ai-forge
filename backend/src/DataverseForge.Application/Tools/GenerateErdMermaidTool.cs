using System.ComponentModel;
using System.Text;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

public class GenerateErdMermaidTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Generate an Entity-Relationship Diagram in Mermaid syntax from the current project state. Shows tables and relationships that have been designed so far.")]
    public async Task<string> GenerateErdMermaidAsync(
        [Description("Optional list of table logical names to include. If empty or null, includes all tables.")] string[]? tableNames,
        [Description("Whether to include field details in the diagram")] bool includeFields,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (state.Tables.Count == 0)
        {
            return "No tables have been designed yet. Use the schema design tools to create tables first, then generate the ERD.";
        }

        var tables = state.Tables;
        var tableNameFilter = tableNames?.Select(n => n.ToLowerInvariant()).ToHashSet();

        if (tableNameFilter is { Count: > 0 })
        {
            tables = tables.Where(t => tableNameFilter.Contains(t.LogicalName.ToLowerInvariant())).ToList();
        }

        if (tables.Count == 0)
        {
            return $"No tables match the specified filter: {string.Join(", ", tableNames ?? [])}";
        }

        var sb = new StringBuilder();
        sb.AppendLine("erDiagram");

        var tableLogicalNames = tables.Select(t => t.LogicalName.ToLowerInvariant()).ToHashSet();

        foreach (var table in tables)
        {
            if (includeFields)
            {
                var fields = state.Fields.Where(f =>
                    f.TableLogicalName.Equals(table.LogicalName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (fields.Count > 0)
                {
                    sb.AppendLine($"    {table.LogicalName} {{");
                    foreach (var field in fields)
                    {
                        sb.AppendLine($"        {field.FieldType} {field.LogicalName} \"{field.DisplayName}\"");
                    }
                    sb.AppendLine("    }");
                }
                else
                {
                    sb.AppendLine($"    {table.LogicalName} {{");
                    sb.AppendLine("    }");
                }
            }
        }

        foreach (var rel in state.Relationships)
        {
            var primaryLower = rel.PrimaryTable.ToLowerInvariant();
            var relatedLower = rel.RelatedTable.ToLowerInvariant();

            if (tableLogicalNames.Contains(primaryLower) && tableLogicalNames.Contains(relatedLower))
            {
                var cardinality = rel.RelationshipType switch
                {
                    "OneToMany" => "||--o{",
                    "ManyToOne" => "}o--||",
                    "ManyToMany" => "}o--o{",
                    _ => "||--||"
                };

                sb.AppendLine($"    {rel.PrimaryTable} {cardinality} {rel.RelatedTable} : \"{rel.SchemaName}\"");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
