using System.ComponentModel;
using System.Text;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

public class GenerateDataDictionaryTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Generate a data dictionary in Markdown format from the current project state. "
        + "Lists all tables with their fields (type, required level, description) and relationships.")]
    public async Task<string> GenerateDataDictionaryAsync(
        [Description("Whether to include relationships in the output")] bool includeRelationships,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        if (state.Tables.Count == 0)
        {
            return "No tables have been created yet. Nothing to document.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Data Dictionary");
        sb.AppendLine();
        sb.AppendLine($"**Solution:** {state.SolutionDisplayName} ({state.SolutionUniqueName})");
        sb.AppendLine($"**Publisher:** {state.PublisherDisplayName} (prefix: {state.PublisherPrefix})");
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();

        foreach (var table in state.Tables)
        {
            sb.AppendLine($"## {table.DisplayName} (`{table.LogicalName}`)");
            sb.AppendLine();

            var fields = state.Fields
                .Where(f => f.TableLogicalName.Equals(table.LogicalName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (fields.Count > 0)
            {
                sb.AppendLine("| Field | Logical Name | Type |");
                sb.AppendLine("|-------|-------------|------|");
                foreach (var field in fields)
                {
                    sb.AppendLine($"| {field.DisplayName} | `{field.LogicalName}` | {field.FieldType} |");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("*No custom fields defined yet.*");
                sb.AppendLine();
            }
        }

        if (includeRelationships && state.Relationships.Count > 0)
        {
            sb.AppendLine("## Relationships");
            sb.AppendLine();
            sb.AppendLine("| Schema Name | Primary Table | Related Table | Type |");
            sb.AppendLine("|-------------|---------------|---------------|------|");
            foreach (var rel in state.Relationships)
            {
                sb.AppendLine($"| `{rel.SchemaName}` | {rel.PrimaryTable} | {rel.RelatedTable} | {rel.RelationshipType} |");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
