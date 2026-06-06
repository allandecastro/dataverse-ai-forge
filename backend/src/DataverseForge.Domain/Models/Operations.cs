namespace DataverseForge.Domain.Models;

public class ExecutedOperation
{
    public string OperationId { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public object? Parameters { get; set; }
    public object? Result { get; set; }
    public bool IsReversible { get; set; }
    public ReverseOperation? ReverseOp { get; set; }
}

public class ReverseOperation
{
    public string ToolName { get; set; } = string.Empty;
    public object? Parameters { get; set; }
    public string Warning { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; }
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public class ProposedChange
{
    public string ChangeId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public object? Parameters { get; set; }
    public RiskLevel RiskLevel { get; set; }
    public DateTime ProposedAt { get; set; }
}

public class AuditEntry
{
    public string EntryId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public bool Success { get; set; }

    /// <summary>Factory for a successful audit entry (auto-fills EntryId + UtcNow timestamp).</summary>
    public static AuditEntry Ok(string agentName, string action, string detail) =>
        new() { EntryId = Guid.NewGuid().ToString("N"), Timestamp = DateTime.UtcNow, AgentName = agentName, Action = action, Detail = detail, Success = true };

    /// <summary>Factory for a failed audit entry (auto-fills EntryId + UtcNow timestamp).</summary>
    public static AuditEntry Failed(string agentName, string action, string detail) =>
        new() { EntryId = Guid.NewGuid().ToString("N"), Timestamp = DateTime.UtcNow, AgentName = agentName, Action = action, Detail = detail, Success = false };
}
