using System.Diagnostics;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using DataverseForge.Application.Telemetry;
using DataverseForge.Domain.Models;
using DataverseForge.Infrastructure.AgUi;
using Microsoft.Extensions.AI;

namespace DataverseForge.Api.Endpoints;

/// <summary>
/// AG-UI SSE chat endpoint and the /api/agui/info discovery endpoint. The SSE
/// path opens a streaming response, runs the agent chain, and emits framed
/// AG-UI events via <see cref="AgUiSseWriter"/>.
/// </summary>
internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        // Discovery — frontend reads this to learn which agents exist.
        app.MapPost("/api/agui/info", () => Results.Ok(new
        {
            actions = Array.Empty<object>(),
            agents = new[] { new { name = "dataverse_forge", description = "AI-powered Dataverse solution builder" } }
        }));

        app.MapPost("/api/agui", async (
            HttpContext ctx,
            OrchestratingChatClient client,
            IProjectStateStore projectStateStore,
            ILogger<OrchestratingChatClient> logger,
            CancellationToken ct) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body, cancellationToken: ct);

            var threadId = body.TryGetProperty("threadId", out var tid)
                ? tid.GetString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");
            var runId = body.TryGetProperty("runId", out var rid)
                ? rid.GetString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");

            var messages = new List<ChatMessage>();
            if (body.TryGetProperty("messages", out var messagesArray))
            {
                foreach (var msg in messagesArray.EnumerateArray())
                {
                    var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "user" : "user";
                    var content = msg.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                    messages.Add(new ChatMessage(new ChatRole(role), content));
                }
            }

            var options = new ChatOptions
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["threadId"] = threadId,
                    ["runId"] = runId
                }
            };

            var projectState = await projectStateStore.GetOrCreateAsync(threadId, ct);
            var agentName = projectState.ActiveAgentName;

            logger.LogInformation("SSE stream started: session={SessionId}, agent={Agent}, messages={MessageCount}",
                threadId, agentName, messages.Count);

            using var activity = ForgeActivitySource.Source.StartActivity("SSE Stream");
            activity?.SetTag("session.id", threadId);
            activity?.SetTag("agent.name", agentName);
            activity?.SetTag("message.count", messages.Count);

            var sw = Stopwatch.StartNew();

            // Build solution state for STATE_DELTA so the frontend can show live data.
            // Same shape used both for the initial snapshot and the post-stream refresh.
            static object BuildSolutionState(ProjectState s) => new
            {
                environmentName = s.EnvironmentName,
                environmentUrl = s.EnvironmentUrl,
                solutionName = s.SolutionDisplayName,
                solutionPrefix = s.PublisherPrefix,
                solutionVersion = s.SolutionVersion,
                tables = s.Tables.Select(t => new
                {
                    logicalName = t.LogicalName,
                    displayName = t.DisplayName,
                    fields = s.Fields
                        .Where(f => f.TableLogicalName == t.LogicalName)
                        .Select(f => new { logicalName = f.LogicalName, displayName = f.DisplayName, fieldType = f.FieldType })
                        .ToArray()
                }).ToArray(),
                relationships = s.Relationships.Select(r => new
                {
                    schemaName = r.SchemaName,
                    primaryTable = r.PrimaryTable,
                    relatedTable = r.RelatedTable,
                    relationshipType = r.RelationshipType
                }).ToArray()
            };
            var solutionState = BuildSolutionState(projectState);

            // Refresher: re-fetch ProjectState after the agent's tool calls finish so the
            // sidebar reflects SelectSolution / CreateSolution / ExecuteCreate* mutations.
            Func<Task<object>> refreshSolutionState = async () =>
            {
                var latest = await projectStateStore.GetOrCreateAsync(threadId, ct);
                return BuildSolutionState(latest);
            };

            try
            {
                var stream = client.GetStreamingResponseAsync(messages, options, ct);
                await AgUiSseWriter.WriteStreamAsync(ctx.Response, threadId, runId, agentName, stream, ct, solutionState, refreshSolutionState);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
            {
                // AI not configured — send friendly error as SSE text message
                await AgUiSseWriter.WriteErrorAsStreamAsync(ctx.Response, threadId, runId,
                    "Azure AI is not configured. Please open Settings and enter your credentials.", ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SSE stream error: session={SessionId}, agent={Agent}", threadId, agentName);

                if (!ctx.Response.HasStarted)
                {
                    ctx.Response.StatusCode = 500;
                }
                else
                {
                    // Do NOT surface ex.Message to the client — exception text can
                    // carry connection strings, endpoint URLs, stack frames, or
                    // upstream provider detail. The full exception is already in
                    // the structured log above (correlatable via session id); the
                    // client gets a generic, actionable message only.
                    await AgUiSseWriter.WriteErrorAsStreamAsync(ctx.Response, threadId, runId,
                        "The agent hit an unexpected error and couldn't finish this turn. "
                        + "Please try again — if it keeps happening, check the backend logs for details.", ct);
                }
            }

            sw.Stop();
            ForgeActivitySource.SseStreamDuration.Record(sw.ElapsedMilliseconds, new KeyValuePair<string, object?>("agent", agentName));
            ForgeActivitySource.AgentTurns.Add(1, new KeyValuePair<string, object?>("agent", agentName));

            logger.LogInformation("SSE stream completed: session={SessionId}, agent={Agent}, duration={Duration}ms",
                threadId, agentName, sw.ElapsedMilliseconds);
        })
        // Names the "agui" fixed-window policy registered in Program.cs. The
        // string is the same policy key — keep them in sync.
        .RequireRateLimiting("agui");

        return app;
    }
}
