using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;

namespace DataverseForge.Api.Endpoints;

/// <summary>
/// Read-only views over per-session audit log + operation stack.
/// Powers the Activity Log panel in the diagnostics drawer.
/// </summary>
internal static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/projects/{sessionId}/audit", async (
            string sessionId,
            IProjectStateStore projectStateStore,
            string? agent,
            string? action,
            bool? success,
            CancellationToken ct) =>
        {
            var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
            IEnumerable<AuditEntry> entries = state.AuditLog;

            if (!string.IsNullOrEmpty(agent))
                entries = entries.Where(e => e.AgentName.Equals(agent, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(action))
                entries = entries.Where(e => e.Action.Contains(action, StringComparison.OrdinalIgnoreCase));
            if (success.HasValue)
                entries = entries.Where(e => e.Success == success.Value);

            return Results.Ok(new
            {
                sessionId,
                count = entries.Count(),
                entries = entries.OrderByDescending(e => e.Timestamp).ToList()
            });
        });

        app.MapGet("/api/projects/{sessionId}/operations", async (
            string sessionId,
            IProjectStateStore projectStateStore,
            CancellationToken ct) =>
        {
            var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
            var operations = state.OperationStack.Select(op => new
            {
                op.OperationId,
                op.ExecutedAt,
                op.AgentName,
                op.ToolName,
                op.IsReversible,
                reverseToolName = op.ReverseOp?.ToolName,
                riskLevel = op.ReverseOp?.RiskLevel.ToString()
            }).ToList();

            return Results.Ok(new
            {
                sessionId,
                count = operations.Count,
                operations
            });
        });

        return app;
    }
}
