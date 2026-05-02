/**
 * Pure helpers for chat message + tool-call bookkeeping. Lives outside the
 * useForgeChat hook so it can be unit-tested without DOM/streaming setup.
 *
 * Symptoms when this logic regressed in the past:
 *   - Tester's tool calls visually attached to DataModelBuilder's bubble (orphan
 *     tools attached to the latest bubble by timestamp instead of by agent).
 */

import type { ForgeMessage, ToolCallInfo } from "@/lib/sse-types";

/**
 * Drop the last message if it's an assistant bubble with whitespace-only
 * content. Used by Stop — the user can hit Stop after TEXT_MESSAGE_START
 * arrives but before any TEXT_MESSAGE_CONTENT, leaving the opened bubble
 * empty in the chat. Trailing-only because earlier empty bubbles are kept
 * (they may be tool-call anchors via attributeToolCalls).
 */
export function dropEmptyTrailingBubble(messages: ForgeMessage[]): ForgeMessage[] {
  if (messages.length === 0) return messages;
  const last = messages[messages.length - 1];
  if (last.role === "assistant" && last.content.trim().length === 0) {
    return messages.slice(0, -1);
  }
  return messages;
}

/**
 * Group tool calls by the assistant bubble they should render under.
 * Strategy:
 *   1. If tc.messageId points at an existing bubble of the SAME agent, use it.
 *   2. Otherwise (orphan), find the nearest bubble for tc.agent — forward-first
 *      (chained agents fire tools BEFORE their text bubble opens), then backward.
 *   3. Last-resort: nearest bubble by timestamp regardless of agent (so tools
 *      never disappear silently).
 *
 * Returns a Map keyed by bubble messageId.
 */
export function attributeToolCalls(
  messages: ForgeMessage[],
  toolCalls: ToolCallInfo[],
): Map<string, ToolCallInfo[]> {
  const result = new Map<string, ToolCallInfo[]>();
  const knownIds = new Set(messages.map((m) => m.id));
  const assistantBubbles = messages
    .filter((m) => m.role === "assistant")
    .map((m) => ({ id: m.id, t: m.timestamp, agent: m.agentName }));

  function nearestSameAgent(t: number, agent: string | undefined): string | null {
    if (!agent) return null;
    const forward = assistantBubbles.find((b) => b.agent === agent && b.t >= t);
    if (forward) return forward.id;
    let backward: string | null = null;
    for (const b of assistantBubbles) {
      if (b.agent === agent && b.t <= t) backward = b.id;
      else if (b.t > t) break;
    }
    return backward;
  }

  function nearestAnyAgent(t: number): string | null {
    let chosen: string | null = null;
    for (const b of assistantBubbles) {
      if (b.t <= t) chosen = b.id;
      else break;
    }
    return chosen ?? assistantBubbles[0]?.id ?? null;
  }

  for (const tc of toolCalls) {
    let target: string | null = null;
    if (tc.messageId && knownIds.has(tc.messageId)) {
      const declared = assistantBubbles.find((b) => b.id === tc.messageId);
      if (declared && (!tc.agent || !declared.agent || declared.agent === tc.agent)) {
        target = tc.messageId;
      }
    }
    target ??= nearestSameAgent(tc.startTime, tc.agent) ?? nearestAnyAgent(tc.startTime);
    if (!target) continue;
    const existing = result.get(target) ?? [];
    existing.push(tc);
    result.set(target, existing);
  }
  return result;
}
