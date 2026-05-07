using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;

namespace DataverseForge.Api.Endpoints;

/// <summary>
/// CRUD over per-session ProjectState. List, rename, create, delete.
/// Backed by IProjectStateStore (in-memory).
/// </summary>
internal static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sessions", async (IProjectStateStore projectStateStore, CancellationToken ct) =>
        {
            var sessions = await projectStateStore.ListSessionsAsync(ct);
            return Results.Ok(new { count = sessions.Count, sessions });
        });

        app.MapDelete("/api/sessions/{sessionId}", async (string sessionId, IProjectStateStore projectStateStore, CancellationToken ct) =>
        {
            await projectStateStore.DeleteSessionAsync(sessionId, ct);
            return Results.NoContent();
        });

        app.MapPut("/api/sessions/{sessionId}", async (
            string sessionId,
            RenameSessionDto dto,
            IProjectStateStore projectStateStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ProjectName))
                return Results.BadRequest(new { error = "projectName is required" });

            var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
            state.ProjectName = dto.ProjectName.Trim();
            state.LastActivityAt = DateTime.UtcNow;
            await projectStateStore.SaveAsync(sessionId, state, ct);
            return Results.Ok(new { sessionId, projectName = state.ProjectName });
        });

        app.MapPost("/api/sessions", async (
            CreateSessionDto dto,
            IProjectStateStore projectStateStore,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ProjectName))
                return Results.BadRequest(new { error = "projectName is required" });

            var sessionId = Guid.NewGuid().ToString("N");
            var state = await projectStateStore.GetOrCreateAsync(sessionId, ct);
            state.ProjectName = dto.ProjectName.Trim();
            state.CreatedAt = DateTime.UtcNow;
            state.LastActivityAt = DateTime.UtcNow;
            await projectStateStore.SaveAsync(sessionId, state, ct);
            return Results.Ok(new { sessionId, projectName = state.ProjectName });
        });

        return app;
    }
}
