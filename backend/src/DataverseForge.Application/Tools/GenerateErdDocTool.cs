using System.ComponentModel;
using DataverseForge.Application.Contracts;

namespace DataverseForge.Application.Tools;

public class GenerateErdDocTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    private readonly GenerateErdMermaidTool _inner = new(projectStateStore, sessionContext);

    [Description("Generate an Entity-Relationship Diagram document in Mermaid syntax from the current project state. "
        + "Wraps the ERD generation tool with documentation-focused defaults (all tables, fields included).")]
    public async Task<string> GenerateErdDocAsync(
        [Description("Whether to include field details in the ERD")] bool includeFields,
        CancellationToken cancellationToken)
    {
        return await _inner.GenerateErdMermaidAsync(null, includeFields, cancellationToken);
    }
}
