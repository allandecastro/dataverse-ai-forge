using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Domain.Models;
using DataverseForge.Infrastructure.Concurrency;
using DataverseForge.Infrastructure.Rag;
using Microsoft.Extensions.AI;

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

        app.MapDelete("/api/sessions/{sessionId}", async (
            string sessionId, IProjectStateStore projectStateStore, RagStoreProvider ragStores, SessionLocks sessionLocks, CancellationToken ct) =>
        {
            // Take the session lock so a delete can't race an in-flight turn's
            // state write (which would resurrect the row).
            using var sessionLock = await sessionLocks.TryAcquireAsync(sessionId, TimeSpan.FromSeconds(3), ct);
            if (sessionLock is null)
                return Results.Conflict(new { error = "This project is busy with another request. Try again in a moment." });

            await projectStateStore.DeleteSessionAsync(sessionId, ct);
            // The project's knowledge base goes with it.
            ragStores.DeleteProject(sessionId);
            return Results.NoContent();
        });

        app.MapPut("/api/sessions/{sessionId}", async (
            string sessionId,
            RenameSessionDto dto,
            IProjectStateStore projectStateStore,
            SessionLocks sessionLocks,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.ProjectName))
                return Results.BadRequest(new { error = "projectName is required" });

            using var sessionLock = await sessionLocks.TryAcquireAsync(sessionId, TimeSpan.FromSeconds(3), ct);
            if (sessionLock is null)
                return Results.Conflict(new { error = "This project is busy with another request. Try again in a moment." });

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

        // Clear a session: wipe its persisted data (project state, LLM
        // conversation, and UI transcript) while keeping the session id + name,
        // so "Clear session" in the UI actually resets what's on disk too.
        app.MapPost("/api/sessions/{sessionId}/clear", async (
            string sessionId,
            IProjectStateStore projectStateStore,
            IConversationStore conversationStore,
            ITranscriptStore transcripts,
            SessionLocks sessionLocks,
            CancellationToken ct) =>
        {
            using var sessionLock = await sessionLocks.TryAcquireAsync(sessionId, TimeSpan.FromSeconds(3), ct);
            if (sessionLock is null)
                return Results.Conflict(new { error = "This project is busy with another request. Try again in a moment." });

            // Reset the business state to a fresh default, keeping identity.
            var existing = await projectStateStore.GetOrCreateAsync(sessionId, ct);
            var fresh = new ProjectState
            {
                ProjectId = sessionId,
                ProjectName = existing.ProjectName,
                CreatedAt = existing.CreatedAt,
                LastActivityAt = DateTime.UtcNow,
            };
            await projectStateStore.SaveAsync(sessionId, fresh, ct);
            await conversationStore.SaveAsync(sessionId, new List<ChatMessage>(), ct);
            await transcripts.SaveAsync(sessionId, "[]", ct);
            return Results.NoContent();
        });

        // Chat transcript (the rendered ForgeMessage[]) persisted server-side so
        // history survives a restart and is available across browsers/devices.
        // The body is stored opaquely — the frontend owns the shape.
        app.MapGet("/api/sessions/{sessionId}/transcript", async (
            string sessionId, ITranscriptStore transcripts, CancellationToken ct) =>
        {
            var json = await transcripts.GetAsync(sessionId, ct);
            return Results.Content(json, "application/json");
        });

        app.MapPut("/api/sessions/{sessionId}/transcript", async (
            string sessionId, HttpRequest request, ITranscriptStore transcripts, CancellationToken ct) =>
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            // Guard: the frontend owns the shape ({ messages, toolCalls } object,
            // or a legacy messages array) — reject anything that isn't valid JSON
            // of one of those container kinds so we never store garbage.
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var kind = doc.RootElement.ValueKind;
                if (kind != System.Text.Json.JsonValueKind.Array && kind != System.Text.Json.JsonValueKind.Object)
                    return Results.BadRequest(new { error = "transcript must be a JSON array or object" });
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.BadRequest(new { error = "transcript must be valid JSON" });
            }

            await transcripts.SaveAsync(sessionId, body, ct);
            return Results.NoContent();
        });

        return app;
    }
}
