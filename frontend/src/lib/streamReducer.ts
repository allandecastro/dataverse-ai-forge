/**
 * Pure reducer for the AG-UI SSE event stream.
 *
 * Replaces the in-line state machine that used to live in useForgeChat — the
 * closure-based design (let activeMessageId / let activeContent / etc.) was
 * impossible to unit test, so subtle regressions in chained-handoff bubble
 * attribution kept slipping through.
 *
 * The reducer combines:
 *   • Persistent UI state — messages, toolCalls, agentState, pendingApproval, lastUsage
 *   • Per-turn transient state — activeMessageId, lastBubbleId, activeContent,
 *     currentAgentName, runToolCalls (which bubble does a fresh tool call attach to?)
 *
 * Both go in one object so a single dispatch can cover everything that needs to
 * change for one SSE event. The hook's `for await` loop becomes:
 *
 *     state = streamReducer(state, event, Date.now());
 *     setMessages(state.messages); setToolCalls(state.toolCalls); ...
 *
 * `now` is injected (rather than calling Date.now() inside) so tests can pin
 * timestamps. SSEEvent unions are exhaustive — adding a new event type forces a
 * compile error here.
 */

import { parseApprovalToolCall } from "./approvalParser";
import { classifyToolResultStatus, shouldKeepBubble } from "./streamingState";
import type {
  AgentState,
  ApprovalRequest,
  ForgeMessage,
  SSEEvent,
  ToolCallInfo,
  UsageInfo,
} from "./sse-types";

export interface StreamState {
  // Persistent — surfaces to React state via setX in the hook
  messages: ForgeMessage[];
  toolCalls: ToolCallInfo[];
  agentState: AgentState;
  pendingApproval: ApprovalRequest | null;
  lastUsage: UsageInfo | null;

  // Transient — only meaningful inside one turn's stream
  activeMessageId: string | null;
  lastBubbleId: string | null;
  activeContent: string;
  currentAgentName: string;
  runToolCalls: Map<string, ToolCallInfo>;
}

export function initStreamState(opts: {
  messages: ForgeMessage[];
  toolCalls: ToolCallInfo[];
  agentState: AgentState;
  pendingApproval: ApprovalRequest | null;
  lastUsage: UsageInfo | null;
  initialAgentName: string;
}): StreamState {
  return {
    messages: opts.messages,
    toolCalls: opts.toolCalls,
    agentState: opts.agentState,
    pendingApproval: opts.pendingApproval,
    lastUsage: opts.lastUsage,
    activeMessageId: null,
    lastBubbleId: null,
    activeContent: "",
    currentAgentName: opts.initialAgentName,
    runToolCalls: new Map(),
  };
}

export function streamReducer(state: StreamState, event: SSEEvent, now: number): StreamState {
  switch (event.type) {
    case "STATE_DELTA":
      return applyStateDelta(state, event.delta);

    case "TEXT_MESSAGE_START":
      return openBubble(state, event.messageId, event.agentName, now);

    case "TEXT_MESSAGE_CONTENT":
      return appendContent(state, event.messageId, event.delta, now);

    case "TEXT_MESSAGE_END":
      return closeBubble(state, event.messageId);

    case "TOOL_CALL_START":
      return startToolCall(state, event.toolCallId, event.toolCallName, event.agentName, now);

    case "TOOL_CALL_ARGS":
      return appendToolArgs(state, event.toolCallId, event.delta);

    case "TOOL_CALL_RESULT":
      return finishToolCall(state, event.toolCallId, event.result, now);

    case "RUN_FINISHED":
      // Deliberately NOT sweeping bubbles here — TEXT_MESSAGE_END drops empties
      // at close time, and a global RUN_FINISHED sweep used to delete bubbles
      // whose tools were attached via attributeToolCalls's nearest-agent rule
      // (those tools don't carry the bubble's id in tc.messageId).
      return event.metadata?.usage
        ? { ...state, lastUsage: event.metadata.usage }
        : state;

    // RUN_STARTED / TOOL_CALL_END are lifecycle markers — no state change.
    case "RUN_STARTED":
    case "TOOL_CALL_END":
      return state;
  }
}

function applyStateDelta(
  state: StreamState,
  delta: ReadonlyArray<{ op: string; path: string; value: unknown }>,
): StreamState {
  let next = state;
  for (const patch of delta) {
    if (patch.path === "/activeAgent" && typeof patch.value === "string") {
      next = {
        ...next,
        currentAgentName: patch.value,
        agentState: { ...next.agentState, activeAgent: patch.value },
      };
    } else if (patch.path === "/reasoning" && typeof patch.value === "string") {
      next = { ...next, agentState: { ...next.agentState, reasoning: patch.value } };
    } else if (patch.path === "/solutionState" && patch.value != null) {
      next = {
        ...next,
        agentState: {
          ...next.agentState,
          solutionState: patch.value as AgentState["solutionState"],
        },
      };
    } else if (patch.path === "/suggestedReplies") {
      // Backend emits an empty array at RUN_STARTED to clear stale chips, and
      // the `suggest_replies` tool replaces the array with up to 4 strings.
      const replies = Array.isArray(patch.value)
        ? (patch.value as unknown[]).filter((v): v is string => typeof v === "string")
        : [];
      next = {
        ...next,
        agentState: { ...next.agentState, suggestedReplies: replies },
      };
    }
  }
  return next;
}

/** Marker prefix for bubbles created by startToolCall *before* TEXT_MESSAGE_START
 * arrives. Lets openBubble recognize and recycle them, and lets persistence
 * filter them out on reload. */
export const PLACEHOLDER_PREFIX = "placeholder-";

function openBubble(
  state: StreamState,
  messageId: string,
  agentName: string | undefined,
  now: number,
): StreamState {
  // Server attributes each bubble to a specific agent — falls back to whatever
  // agent the orchestrator most recently signalled via STATE_DELTA.
  const agent = agentName ?? state.currentAgentName;

  // Recycle a placeholder bubble created by startToolCall for this same agent.
  // Past behavior: tools fired before TEXT_MESSAGE_START attached to the
  // *previous* agent's bubble, then visually "jumped" once the new bubble
  // opened. Pre-creating a placeholder keeps the tools rooted under the right
  // agent from the start; recycling here folds the real messageId into that
  // same row so we don't end up with two stacked bubbles.
  const lastIdx = state.messages.length - 1;
  const last = lastIdx >= 0 ? state.messages[lastIdx] : null;
  if (
    last &&
    last.id.startsWith(PLACEHOLDER_PREFIX) &&
    last.agentName === agent &&
    last.content === ""
  ) {
    const oldId = last.id;
    const recycled = state.messages.slice();
    recycled[lastIdx] = { ...last, id: messageId, timestamp: now };
    return {
      ...state,
      activeMessageId: messageId,
      lastBubbleId: messageId,
      activeContent: "",
      currentAgentName: agent,
      messages: recycled,
      toolCalls: state.toolCalls.map((tc) =>
        tc.messageId === oldId ? { ...tc, messageId } : tc,
      ),
    };
  }

  return {
    ...state,
    activeMessageId: messageId,
    lastBubbleId: messageId,
    activeContent: "",
    currentAgentName: agent,
    messages: [
      ...state.messages,
      { id: messageId, role: "assistant", content: "", timestamp: now, agentName: agent },
    ],
  };
}

function appendContent(
  state: StreamState,
  messageId: string,
  delta: string,
  now: number,
): StreamState {
  // Defensive: server didn't open the bubble — open it now so the content has a home.
  let next = state;
  if (next.activeMessageId !== messageId) {
    const exists = next.messages.some((m) => m.id === messageId);
    next = {
      ...next,
      activeMessageId: messageId,
      activeContent: "",
      messages: exists
        ? next.messages
        : [
            ...next.messages,
            {
              id: messageId,
              role: "assistant",
              content: "",
              timestamp: now,
              agentName: next.currentAgentName,
            },
          ],
    };
  }

  const newContent = next.activeContent + delta;
  return {
    ...next,
    activeContent: newContent,
    messages: next.messages.map((m) =>
      m.id === messageId
        ? { ...m, content: newContent, agentName: next.currentAgentName }
        : m,
    ),
  };
}

function closeBubble(state: StreamState, messageId: string): StreamState {
  const closedContent = state.activeContent;
  const wasActive = state.activeMessageId === messageId;

  let next = state;
  if (wasActive) {
    next = { ...next, activeMessageId: null, activeContent: "" };
  }

  if (!shouldKeepBubble(closedContent)) {
    // Don't drop a bubble that has tool calls attached — those tools would
    // lose their anchor (the recycled placeholder case: tools landed here
    // before TEXT_MESSAGE_START, the agent then opened+closed the bubble
    // without speaking). The tool-call rows themselves render as content.
    const hasTools = state.toolCalls.some((tc) => tc.messageId === messageId);
    if (!hasTools) {
      return {
        ...next,
        messages: next.messages.filter((m) => m.id !== messageId),
        // Don't keep an empty bubble as the tool-call anchor either.
        lastBubbleId: next.lastBubbleId === messageId ? null : next.lastBubbleId,
      };
    }
  }
  return next;
}

function startToolCall(
  state: StreamState,
  toolCallId: string,
  name: string,
  agent: string | undefined,
  now: number,
): StreamState {
  // Decide which bubble this tool attaches to.
  //
  // The naive "attach to activeMessageId or lastBubbleId" path causes a visual
  // jump: when an agent fires tools BEFORE its TEXT_MESSAGE_START arrives, the
  // tools land on the *previous* agent's bubble, then leap to the new bubble
  // once it opens. To avoid that we pre-create an empty placeholder bubble for
  // the new agent and attach the tool to it. openBubble recycles this
  // placeholder's slot when the real TEXT_MESSAGE_START finally arrives.
  //
  // Rule: if there's an actively-open bubble whose agent matches this tool,
  // attach to it. Otherwise we're starting a fresh turn (chained handoff,
  // post-user-input continuation, or different agent) — create or reuse a
  // placeholder for this agent.
  //
  // When `agent` is undefined the SSE event didn't tell us who fired the tool;
  // fall back to the legacy active-or-last attach so we never crash.
  let next = state;
  let attachToId: string | undefined = state.activeMessageId ?? state.lastBubbleId ?? undefined;

  if (agent) {
    const activeBubble = next.activeMessageId
      ? next.messages.find((m) => m.id === next.activeMessageId)
      : null;
    const activeMatchesAgent = activeBubble?.agentName === agent;

    if (!activeMatchesAgent) {
      // Reuse a recent same-agent placeholder if one already exists (multi-tool
      // burst within the same pre-bubble window) — without this, every TOOL_CALL_START
      // would spawn a fresh placeholder and the chat would fill with empty rows.
      const lastIdx = next.messages.length - 1;
      const last = lastIdx >= 0 ? next.messages[lastIdx] : null;
      const reuseLast =
        last !== null &&
        last.id.startsWith(PLACEHOLDER_PREFIX) &&
        last.agentName === agent &&
        last.content === "";

      if (reuseLast && last) {
        attachToId = last.id;
      } else {
        const placeholderId = `${PLACEHOLDER_PREFIX}${toolCallId}`;
        next = {
          ...next,
          messages: [
            ...next.messages,
            { id: placeholderId, role: "assistant", content: "", timestamp: now, agentName: agent },
          ],
          lastBubbleId: placeholderId,
        };
        attachToId = placeholderId;
      }
    } else {
      // Active bubble matches the agent — attach there (keep activeMessageId).
      attachToId = next.activeMessageId ?? attachToId;
    }
  }

  const tc: ToolCallInfo = {
    id: toolCallId,
    name,
    args: "",
    status: "running",
    startTime: now,
    messageId: attachToId,
    agent,
  };
  const newRunMap = new Map(next.runToolCalls);
  newRunMap.set(toolCallId, tc);
  return {
    ...next,
    runToolCalls: newRunMap,
    toolCalls: [...next.toolCalls, tc],
  };
}

function appendToolArgs(state: StreamState, toolCallId: string, delta: string): StreamState {
  const existing = state.runToolCalls.get(toolCallId);
  if (!existing) return state;
  const updated = { ...existing, args: existing.args + delta };
  const newRunMap = new Map(state.runToolCalls);
  newRunMap.set(toolCallId, updated);
  return {
    ...state,
    runToolCalls: newRunMap,
    toolCalls: state.toolCalls.map((t) => (t.id === toolCallId ? updated : t)),
  };
}

function finishToolCall(
  state: StreamState,
  toolCallId: string,
  result: string,
  now: number,
): StreamState {
  const existing = state.runToolCalls.get(toolCallId);
  if (!existing) return state;

  const status = classifyToolResultStatus(result);
  const updated: ToolCallInfo = { ...existing, result, status, endTime: now };
  const newRunMap = new Map(state.runToolCalls);
  newRunMap.set(toolCallId, updated);

  // Detect approval-shaped tool calls (request_approval / request_batch_approval)
  // and surface the parsed request so the UI can pop an approval card.
  const approval = parseApprovalToolCall(updated);

  return {
    ...state,
    runToolCalls: newRunMap,
    toolCalls: state.toolCalls.map((t) => (t.id === toolCallId ? updated : t)),
    pendingApproval: approval ?? state.pendingApproval,
  };
}
