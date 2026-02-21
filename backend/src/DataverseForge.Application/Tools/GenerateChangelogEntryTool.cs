using System.ComponentModel;
using System.Text;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

public class GenerateChangelogEntryTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Generate a changelog entry in Markdown format from the operation history. "
        + "Reads the OperationStack from ProjectState and produces Added/Changed/Removed sections.")]
    public async Task<string> GenerateChangelogEntryAsync(
        [Description("Version label for this changelog entry (e.g. '1.0.0')")] string version,
        CancellationToken cancellationToken)
    {
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);

        var operations = state.OperationStack.ToList();
        if (operations.Count == 0)
        {
            return "No operations have been executed yet. Nothing to document.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## [{version}] - {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();

        var added = operations
            .Where(op => op.ToolName.StartsWith("execute_create", StringComparison.OrdinalIgnoreCase)
                      || op.ToolName.StartsWith("ExecuteCreate", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var modified = operations
            .Where(op => op.ToolName.StartsWith("execute_modify", StringComparison.OrdinalIgnoreCase)
                      || op.ToolName.StartsWith("ExecuteModify", StringComparison.OrdinalIgnoreCase)
                      || op.ToolName.StartsWith("execute_update", StringComparison.OrdinalIgnoreCase)
                      || op.ToolName.StartsWith("ExecuteUpdate", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var removed = operations
            .Where(op => op.ToolName.StartsWith("execute_delete", StringComparison.OrdinalIgnoreCase)
                      || op.ToolName.StartsWith("ExecuteDelete", StringComparison.OrdinalIgnoreCase)
                      || op.ToolName.StartsWith("execute_remove", StringComparison.OrdinalIgnoreCase)
                      || op.ToolName.StartsWith("ExecuteRemove", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (added.Count > 0)
        {
            sb.AppendLine("### Added");
            foreach (var op in added)
            {
                sb.AppendLine($"- {FormatOperation(op)}");
            }
            sb.AppendLine();
        }

        if (modified.Count > 0)
        {
            sb.AppendLine("### Changed");
            foreach (var op in modified)
            {
                sb.AppendLine($"- {FormatOperation(op)}");
            }
            sb.AppendLine();
        }

        if (removed.Count > 0)
        {
            sb.AppendLine("### Removed");
            foreach (var op in removed)
            {
                sb.AppendLine($"- {FormatOperation(op)}");
            }
            sb.AppendLine();
        }

        // Operations that don't fit the above categories
        var other = operations.Except(added).Except(modified).Except(removed).ToList();
        if (other.Count > 0 && added.Count == 0 && modified.Count == 0 && removed.Count == 0)
        {
            sb.AppendLine("### Changes");
            foreach (var op in other)
            {
                sb.AppendLine($"- {FormatOperation(op)}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatOperation(Domain.Models.ExecutedOperation op)
    {
        var toolName = op.ToolName
            .Replace("execute_create_", "Created ", StringComparison.OrdinalIgnoreCase)
            .Replace("ExecuteCreate", "Created ", StringComparison.OrdinalIgnoreCase)
            .Replace("execute_modify_", "Modified ", StringComparison.OrdinalIgnoreCase)
            .Replace("ExecuteModify", "Modified ", StringComparison.OrdinalIgnoreCase)
            .Replace("execute_delete_", "Deleted ", StringComparison.OrdinalIgnoreCase)
            .Replace("ExecuteDelete", "Deleted ", StringComparison.OrdinalIgnoreCase);

        return $"{toolName} (by {op.AgentName}, {op.ExecutedAt:yyyy-MM-dd HH:mm} UTC)";
    }
}
