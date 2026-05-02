namespace DataverseForge.Domain.Models;

public partial class ProjectState
{
    /// <summary>
    /// Stack of executed write operations for undo support — each entry knows how to
    /// reverse the operation it represents (DeleteTable for ExecuteCreateTable, etc.).
    /// </summary>
    public Stack<ExecutedOperation> OperationStack { get; set; } = new();

    /// <summary>
    /// Append-only audit log: every action an agent took, with success/failure detail.
    /// Used for the Activity Log panel + post-mortem when the user reports a bug.
    /// </summary>
    public List<AuditEntry> AuditLog { get; set; } = [];
}
