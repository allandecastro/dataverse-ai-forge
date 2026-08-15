using System.ComponentModel;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Routes the conversation to the Documentary Agent for knowledge-base operations
/// (list sources, ingest, delete, gap analysis, conflict detection).
/// Available to SA and BA.
/// </summary>
public class HandoffToDocumentaryAgentTool(IProjectStateStore projectStateStore, ISessionContext sessionContext)
{
    [Description("Hand off to the Documentary Agent to manage the project knowledge base: " +
                 "list sources, ingest a new document or repo, delete an obsolete source, " +
                 "run a gap analysis, or detect conflicts between sources. " +
                 "Use whenever the user provides a document to ingest, asks about KB contents, " +
                 "requests a gap or conflict analysis, or wants to remove a source.")]
    public async Task<string> HandoffToDocumentaryAgentAsync(
        [Description("Brief description of the KB operation needed (e.g. 'ingest spec at C:/specs', 'gap analysis spec vs backlog').")] string task = "",
        CancellationToken cancellationToken = default)
    {
        return await HandoffTool.ExecuteHandoffAsync(
            projectStateStore, sessionContext,
            AgentNames.DocumentaryAgent, "Documentary Agent",
            task, cancellationToken);
    }
}
