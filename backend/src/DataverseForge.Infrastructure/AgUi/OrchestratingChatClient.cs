using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Infrastructure.AzureAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Infrastructure.AgUi;

/// <summary>
/// Singleton IChatClient that, per streaming request, opens a fresh DI scope to
/// resolve the active agent (its tools and ChatClient are <c>Scoped</c>), then
/// drives the chained-handoff loop until the chain stops or the user's turn ends.
///
/// Lifetime model — read this before refactoring:
///   * <c>OrchestratingChatClient</c> is registered as a singleton (so every
///     request shares the same instance), but it holds <see cref="IServiceScopeFactory"/>
///     and creates a NEW scope inside <c>GetStreamingResponseAsync</c>. That scope
///     is what materializes the per-agent <c>SolutionArchitectAgent</c>,
///     <c>DataModelBuilderAgent</c>, … instances along with their per-call tools.
///   * Chained handoffs reuse a single scope across the chain so tool state
///     (PendingPlans, OperationStack additions) is consistent within one user
///     turn.
///
/// Why not register the agents themselves as the public surface? The orchestrator
/// also owns the chain-loop policy (cycle guard, forced handoff for Tester,
/// agent-name STATE_DELTA emission). Pushing that into each agent would
/// duplicate the policy and make handoff topology changes painful.
///
/// This class deliberately does NOT inherit any naming from earlier prototypes — if
/// you later add per-thread checkpointing or sub-orchestrators, rename to
/// <c>ChainOrchestratorChatClient</c>; the current name is fine while there's a
/// single implementation.
/// </summary>
public sealed class OrchestratingChatClient(
    IServiceScopeFactory scopeFactory,
    ChatClientFactory chatClientFactory,
    ILogger<OrchestratingChatClient> logger) : IChatClient
{
    private static readonly Dictionary<string, Func<IServiceProvider, (string SystemPrompt, IEnumerable<AITool> Tools)>> AgentResolvers = new()
    {
        [AgentNames.SolutionArchitect] = sp =>
        {
            var agent = sp.GetRequiredService<SolutionArchitectAgent>();
            return (SolutionArchitectPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.BusinessAnalyst] = sp =>
        {
            var agent = sp.GetRequiredService<BusinessAnalystAgent>();
            return (BusinessAnalystPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.DataModelBuilder] = sp =>
        {
            var agent = sp.GetRequiredService<DataModelBuilderAgent>();
            return (DataModelBuilderPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.Tester] = sp =>
        {
            var agent = sp.GetRequiredService<TesterAgent>();
            return (TesterPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.UIBuilder] = sp =>
        {
            var agent = sp.GetRequiredService<UIBuilderAgent>();
            return (UIBuilderPrompt.SystemPrompt, GetAgentTools(agent));
        },
        [AgentNames.DataPopulation] = sp =>
        {
            var agent = sp.GetRequiredService<DataPopulationAgent>();
            return (DataPopulationPrompt.SystemPrompt, GetAgentTools(agent));
        }
    };

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Graceful fallback when AI is not configured
        if (!chatClientFactory.IsConfigured)
        {
            var errorUpdate = new ChatResponseUpdate();
            errorUpdate.Contents.Add(new TextContent(
                "Azure AI is not configured yet. Please open Settings (gear icon in the sidebar) "
                + "and enter your Azure AI Foundry endpoint and API key. The agents will start working "
                + "once credentials are saved."));
            yield return errorUpdate;
            yield break;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var projectStateStore = sp.GetRequiredService<IProjectStateStore>();
        var conversationStore = sp.GetRequiredService<IConversationStore>();
        var sessionContext = sp.GetRequiredService<ISessionContext>();

        // Extract threadId from options or generate one
        var threadId = options?.AdditionalProperties?.TryGetValue("threadId", out var tid) == true
            ? tid?.ToString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        // Per-turn correlation ID for tracing this user message end-to-end through
        // tools and Dataverse calls. The frontend sends one in the body; otherwise we
        // generate a fresh one.
        var runId = options?.AdditionalProperties?.TryGetValue("runId", out var rid) == true
            ? rid?.ToString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        sessionContext.SessionId = threadId;
        sessionContext.RunId = runId;

        var projectState = await projectStateStore.GetOrCreateAsync(threadId, cancellationToken);

        // Drop pending plans older than the TTL — protects against a user closing
        // their tab mid-approval and leaving a stale plan the agent re-finds
        // hours later. Audit-log so the activity panel shows the cleanup.
        var prunedPlanIds = projectState.PruneStalePendingPlans(DateTime.UtcNow);
        if (prunedPlanIds.Count > 0)
        {
            logger.LogInformation("Pruned {Count} stale pending plans (>{Ttl}h old) for session {SessionId}: {PlanIds}",
                prunedPlanIds.Count, Domain.Models.ProjectState.PendingPlanTtl.TotalHours, threadId, string.Join(", ", prunedPlanIds));
            projectState.AuditLog.Add(new Domain.Models.AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = "system",
                Action = "PruneStalePlans",
                Detail = $"Dropped {prunedPlanIds.Count} pending plan(s) older than {Domain.Models.ProjectState.PendingPlanTtl.TotalHours}h: {string.Join(", ", prunedPlanIds)}",
                Success = true
            });
            await projectStateStore.SaveAsync(threadId, projectState, cancellationToken);
        }

        // Previously had an entry-agent guard that snapped ActiveAgentName to
        // SolutionArchitect whenever there were no pending plans. That broke
        // the case where a builder (UIB / DMB) asked the user a clarifying
        // question and the user replied — the reply went to SA instead of back
        // to the builder. The guard's original justification (DMB hallucinating
        // env info on first turn) is now handled by the agent toolset: DMB
        // doesn't have GetEnvironmentInfo, and the "stay in your lane" prompt
        // rule + missing tools force a handoff. Trust the active agent.
        var activeAgentName = projectState.ActiveAgentName;

        // Use BeginScope so every log line emitted for this turn (in this method AND
        // anything called from here, since AsyncLocal-based scopes propagate across
        // awaits) carries SessionId + RunId automatically.
        //
        // Format-string scope (not a raw Dictionary): JsonConsoleFormatter writes
        // both the KVPs *and* a "Message" property = scope.ToString() per scope.
        // Dictionary.ToString() returns the type name — emits useless
        // "System.Collections.Generic.Dictionary`2[...]" in logs. FormattedLogValues
        // (what BeginScope("...", args) returns) renders the substituted template.
        using var logScope = logger.BeginScope("Session={SessionId} Run={RunId}", threadId, runId);
        logger.LogInformation("Streaming request for session {SessionId} run {RunId}, active agent: {AgentName}",
            threadId, runId, activeAgentName);

        var turnStopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (!AgentResolvers.TryGetValue(activeAgentName, out var resolver))
        {
            logger.LogWarning("Agent '{AgentName}' not found, falling back to SolutionArchitect", activeAgentName);
            activeAgentName = AgentNames.SolutionArchitect;
            resolver = AgentResolvers[activeAgentName];
            projectState.ActiveAgentName = activeAgentName;
            await projectStateStore.SaveAsync(threadId, projectState, cancellationToken);
        }

        var (systemPrompt, tools) = resolver(sp);

        // Inject project state into system prompt
        var injectedPrompt = systemPrompt.Replace(
            "[INJECTED AT RUNTIME]",
            JsonSerializer.Serialize(projectState, new JsonSerializerOptions { WriteIndented = true }));

        // Localize replies to the Dataverse environment's base language. Prepended
        // (not appended) because long system prompts often fade in the model's
        // attention — front-of-prompt placement makes the rule stick.
        var languageDirective = LanguageDirective.ForLcid(projectState.BaseLanguageLcid);
        injectedPrompt = languageDirective + "\n\n" + injectedPrompt;

        // Build message list with system prompt
        var history = await conversationStore.GetOrCreateAsync(threadId, cancellationToken);

        if (history.Count > 0 && history[0].Role == ChatRole.System)
        {
            history[0] = new ChatMessage(ChatRole.System, injectedPrompt);
        }
        else
        {
            history.Insert(0, new ChatMessage(ChatRole.System, injectedPrompt));
        }

        // Append incoming user messages (skip system messages already in history)
        foreach (var msg in chatMessages)
        {
            if (msg.Role != ChatRole.System)
            {
                history.Add(msg);
            }
        }

        // Chained handoffs: an agent can call handoff_to_X mid-turn, which updates
        // ProjectState.ActiveAgent. We then run the new agent in the SAME SSE stream so
        // the user sees the next agent's response immediately (e.g. SA hands off to DMB,
        // DMB hands off to Tester — all in one turn). Hard cap to prevent infinite loops.
        const int MaxChainedAgents = 4;
        var visitedAgents = new HashSet<string> { activeAgentName };
        var currentAgentName = activeAgentName;
        var currentTools = tools;

        for (var chainStep = 0; chainStep < MaxChainedAgents; chainStep++)
        {
            // Synthetic update so downstream consumers know which agent's tool calls / text follow.
            yield return new ChatResponseUpdate
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    ["forge_agent_name"] = currentAgentName
                }
            };

            var chatOptions = new ChatOptions { Tools = [.. currentTools] };
            var innerClient = chatClientFactory.GetClientForAgent(currentAgentName);

            // Pass a pruned VIEW of the history to the LLM. The full conversation is
            // still preserved by `history` (and saved at end of turn) — only the
            // input-token surface is bounded. See HistoryPruner.cs for the
            // user-turn-boundary heuristic that keeps tool-call/result pairs intact.
            var historyForLlm = Application.Orchestration.HistoryPruner.PruneForLlm(history);
            if (historyForLlm.Count < history.Count)
            {
                logger.LogDebug("Pruned history for LLM: {Full} → {Trimmed} messages", history.Count, historyForLlm.Count);
            }

            // Accumulate this agent's text content as it streams so we can run
            // PseudoHandoffRecovery on the full reply once the inner stream ends.
            // The user has already SEEN each delta (typewriter effect preserved);
            // this buffer is only used to detect the "the entire reply was a
            // pseudo-handoff function-call string" case and fire the real handoff
            // server-side. Recovery mutates ProjectState.ActiveAgentName, so the
            // chain-loop decision below picks up the corrected target on its next
            // iteration. Without this wiring, the pseudo-handoff text reaches the
            // user, no handoff fires, and the conversation stalls.
            var stepTextBuffer = new StringBuilder();
            await foreach (var update in innerClient.GetStreamingResponseAsync(historyForLlm, chatOptions, cancellationToken))
            {
                if (update.Text is { Length: > 0 } stepText)
                {
                    stepTextBuffer.Append(stepText);
                }
                yield return update;
            }

            // Best-effort pseudo-handoff recovery on the SSE path. Only fires when
            // the entire reply is a pseudo function-call string like
            // `handoff_to_ui_builder(reason="…")`; normal prose is left untouched.
            // This was previously wired only into AgentOrchestrator.ChatAsync (the
            // non-SSE path) — production traffic goes through here, so the guard
            // never ran.
            var stepText2 = stepTextBuffer.ToString();
            if (!string.IsNullOrWhiteSpace(stepText2))
            {
                await PseudoHandoffRecovery.RecoverAsync(
                    stepText2, projectStateStore, sessionContext, logger, cancellationToken);
            }

            // After this agent's stream ends, decide what to do next using the pure
            // chain-loop logic (testable in isolation in ChainLoopDecisions).
            var stateAfter = await projectStateStore.GetOrCreateAsync(threadId, cancellationToken);
            var decision = Application.Orchestration.ChainLoopDecisions.Evaluate(
                currentAgent: currentAgentName,
                activeAgentAfter: stateAfter.ActiveAgentName,
                visitedAgents: visitedAgents,
                agentExists: AgentResolvers.ContainsKey);

            // Apply the decision: persist the chosen NextActiveAgent + write audit if any.
            var beforeApply = stateAfter.ActiveAgentName;
            stateAfter.ActiveAgentName = decision.NextActiveAgent;
            if (decision.AuditAction is not null)
            {
                stateAfter.AuditLog.Add(new Domain.Models.AuditEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    Timestamp = DateTime.UtcNow,
                    AgentName = currentAgentName,
                    Action = decision.AuditAction,
                    Detail = decision.AuditDetail ?? string.Empty,
                    Success = decision.AuditSuccess
                });
            }
            // Save only if we changed something — avoids gratuitous writes when
            // an agent's turn ends cleanly with no handoff and no forced reroute.
            if (beforeApply != decision.NextActiveAgent || decision.AuditAction is not null)
            {
                await projectStateStore.SaveAsync(threadId, stateAfter, cancellationToken);
            }

            if (decision.Step == Application.Orchestration.ChainLoopDecisions.NextStep.Stop)
            {
                if (decision.AuditAction == "ForcedHandoff")
                {
                    logger.LogInformation("{Agent} ended turn without handoff; forcing handoff_to_solution_architect", currentAgentName);
                }
                else if (decision.AuditAction == "HandoffBlocked")
                {
                    logger.LogWarning("Invalid handoff blocked: {FromAgent} → {ToAgent}: {Error}",
                        currentAgentName, beforeApply, decision.AuditDetail);
                }
                else if (visitedAgents.Contains(beforeApply))
                {
                    logger.LogInformation("Skipping chained handoff back to {Agent} (already ran this turn); restoring ActiveAgent to {Current}",
                        beforeApply, currentAgentName);
                }
                else if (!AgentResolvers.ContainsKey(beforeApply) && beforeApply != currentAgentName)
                {
                    logger.LogWarning("Cannot chain to unknown agent {Agent}; restoring ActiveAgent to {Current}",
                        beforeApply, currentAgentName);
                }
                break;
            }

            // Continue the chain — resolve the next agent's tools/prompt.
            visitedAgents.Add(decision.NextActiveAgent);
            if (!AgentResolvers.TryGetValue(decision.NextActiveAgent, out var nextResolver))
            {
                // Should never hit this — ChainLoopDecisions checked agentExists.
                break;
            }

            logger.LogInformation("Chained handoff: {From} → {To} (step {Step})",
                currentAgentName, stateAfter.ActiveAgentName, chainStep + 1);

            // Re-inject the freshly mutated project state into the next agent's prompt,
            // so the new agent sees the latest tables / fields / etc.
            var (nextSystemPrompt, nextTools) = nextResolver(sp);
            var nextInjected = nextSystemPrompt.Replace(
                "[INJECTED AT RUNTIME]",
                JsonSerializer.Serialize(stateAfter, new JsonSerializerOptions { WriteIndented = true }));
            history[0] = new ChatMessage(ChatRole.System, nextInjected);

            currentAgentName = stateAfter.ActiveAgentName;
            currentTools = nextTools;
        }

        // Update state after completion
        var finalState = await projectStateStore.GetOrCreateAsync(threadId, cancellationToken);
        finalState.LastActivityAt = DateTime.UtcNow;
        await projectStateStore.SaveAsync(threadId, finalState, cancellationToken);

        // Persist conversation history
        await conversationStore.SaveAsync(threadId, history, cancellationToken);

        // One-line summary so log scanning ("what just happened in this turn")
        // doesn't require correlating start/end events. Includes the agents
        // visited (chain participants) so a hand-off cycle is visible at a glance.
        turnStopwatch.Stop();
        logger.LogInformation(
            "Turn completed: session={SessionId} run={RunId} duration={Duration}ms agents=[{Agents}] finalAgent={FinalAgent}",
            threadId, runId, turnStopwatch.ElapsedMilliseconds,
            string.Join(" → ", visitedAgents), finalState.ActiveAgentName);
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!chatClientFactory.IsConfigured)
            throw new InvalidOperationException("Azure AI is not configured.");

        return await chatClientFactory.DefaultClient.GetResponseAsync(chatMessages, options, cancellationToken);
    }

    public void Dispose()
    {
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return chatClientFactory.DefaultClient.GetService(serviceType, serviceKey);
    }

    private static IEnumerable<AITool> GetAgentTools(IAgent agent)
    {
        // All agent constructors accept IEnumerable<AITool> tools
        // The primary constructor parameters are stored as synthesized fields
        var toolsField = agent.GetType().GetFields(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType.IsAssignableTo(typeof(IEnumerable<AITool>)));

        if (toolsField != null)
        {
            return (IEnumerable<AITool>)toolsField.GetValue(agent)!;
        }

        return [];
    }
}
