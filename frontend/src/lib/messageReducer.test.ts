import { describe, it, expect } from "vitest";
import { attributeToolCalls, dropEmptyTrailingBubble } from "./messageReducer";
import type { ForgeMessage, ToolCallInfo } from "./sse-types";

function msg(
  partial: Partial<ForgeMessage> & Pick<ForgeMessage, "id" | "role">,
): ForgeMessage {
  return {
    content: "",
    timestamp: 0,
    agentName: undefined,
    ...partial,
  };
}

function tool(
  partial: Partial<ToolCallInfo> & Pick<ToolCallInfo, "id">,
): ToolCallInfo {
  return {
    name: "x",
    args: "",
    status: "complete",
    startTime: 0,
    endTime: 100,
    messageId: undefined,
    agent: undefined,
    ...partial,
  };
}

// =====================================================================
// attributeToolCalls
// =====================================================================

describe("attributeToolCalls", () => {
  it("attaches tool call to declared messageId when agent matches", () => {
    const messages = [msg({ id: "a1", role: "assistant", agentName: "DMB", timestamp: 100 })];
    const tools = [tool({ id: "t1", messageId: "a1", agent: "DMB", startTime: 110 })];
    const map = attributeToolCalls(messages, tools);
    expect(map.get("a1")?.map((t) => t.id)).toEqual(["t1"]);
  });

  it("ignores declared messageId if it points at a different agent's bubble", () => {
    // Symptom: backend bug had Tester's tools tagged with DMB's messageId;
    // the reducer should reject mismatched declared targets and find the right bubble.
    const messages = [
      msg({ id: "a1", role: "assistant", agentName: "DMB", timestamp: 100 }),
      msg({ id: "a2", role: "assistant", agentName: "Tester", timestamp: 200 }),
    ];
    const tools = [
      tool({ id: "t1", messageId: "a1" /* DMB bubble */, agent: "Tester", startTime: 150 }),
    ];
    const map = attributeToolCalls(messages, tools);
    expect(map.get("a1")).toBeUndefined();
    expect(map.get("a2")?.map((t) => t.id)).toEqual(["t1"]);
  });

  it("attaches orphan tool call (no messageId) forward to same-agent bubble", () => {
    // Tester fires verify_* tools BEFORE Tester's bubble opens. Forward-search wins.
    const messages = [
      msg({ id: "a1", role: "assistant", agentName: "DMB", timestamp: 100 }),
      msg({ id: "a2", role: "assistant", agentName: "Tester", timestamp: 200 }),
    ];
    const tools = [tool({ id: "t1", agent: "Tester", startTime: 150 })];
    const map = attributeToolCalls(messages, tools);
    expect(map.get("a2")?.map((t) => t.id)).toEqual(["t1"]);
    expect(map.get("a1")).toBeUndefined();
  });

  it("falls back to backward same-agent bubble when no forward bubble", () => {
    const messages = [
      msg({ id: "a1", role: "assistant", agentName: "Tester", timestamp: 100 }),
      msg({ id: "a2", role: "assistant", agentName: "DMB", timestamp: 200 }),
    ];
    const tools = [tool({ id: "t1", agent: "Tester", startTime: 250 })];
    const map = attributeToolCalls(messages, tools);
    expect(map.get("a1")?.map((t) => t.id)).toEqual(["t1"]);
  });

  it("falls back to nearest-by-time when agent has no bubble at all", () => {
    // Last-resort: never lose a tool call.
    const messages = [
      msg({ id: "a1", role: "assistant", agentName: "DMB", timestamp: 100 }),
    ];
    const tools = [tool({ id: "t1", agent: "Tester", startTime: 120 })];
    const map = attributeToolCalls(messages, tools);
    expect(map.get("a1")?.map((t) => t.id)).toEqual(["t1"]);
  });

  it("groups multiple tools from same agent under the same bubble", () => {
    const messages = [msg({ id: "a1", role: "assistant", agentName: "DMB", timestamp: 100 })];
    const tools = [
      tool({ id: "t1", agent: "DMB", startTime: 110 }),
      tool({ id: "t2", agent: "DMB", startTime: 120 }),
      tool({ id: "t3", agent: "DMB", startTime: 130 }),
    ];
    const map = attributeToolCalls(messages, tools);
    expect(map.get("a1")?.map((t) => t.id)).toEqual(["t1", "t2", "t3"]);
  });

  it("returns empty map when there are no assistant bubbles", () => {
    const messages = [msg({ id: "u1", role: "user", content: "hi" })];
    const tools = [tool({ id: "t1", agent: "DMB", startTime: 100 })];
    const map = attributeToolCalls(messages, tools);
    expect(map.size).toBe(0);
  });
});

// =====================================================================
// dropEmptyTrailingBubble
// =====================================================================

describe("dropEmptyTrailingBubble", () => {
  it("drops a trailing empty assistant bubble", () => {
    // Symptom this protects: user hits Stop after TEXT_MESSAGE_START fired
    // but before any TEXT_MESSAGE_CONTENT — bubble sits empty in the chat.
    const messages = [
      msg({ id: "u1", role: "user", content: "hi" }),
      msg({ id: "a1", role: "assistant", content: "Hello" }),
      msg({ id: "a2", role: "assistant", content: "" }),
    ];
    const result = dropEmptyTrailingBubble(messages);
    expect(result).toHaveLength(2);
    expect(result.map((m) => m.id)).toEqual(["u1", "a1"]);
  });

  it("drops a trailing whitespace-only assistant bubble", () => {
    const messages = [
      msg({ id: "u1", role: "user", content: "hi" }),
      msg({ id: "a1", role: "assistant", content: "   \n\t " }),
    ];
    expect(dropEmptyTrailingBubble(messages)).toHaveLength(1);
  });

  it("keeps a trailing assistant bubble that has any visible content", () => {
    const messages = [
      msg({ id: "u1", role: "user", content: "hi" }),
      msg({ id: "a1", role: "assistant", content: "Working on it." }),
    ];
    expect(dropEmptyTrailingBubble(messages)).toEqual(messages);
  });

  it("does NOT drop earlier empty bubbles — only the trailing one", () => {
    // Earlier empty bubbles can be tool-call anchors via attributeToolCalls's
    // forward-search rule. Only the very last one is safe to drop.
    const messages = [
      msg({ id: "u1", role: "user", content: "hi" }),
      msg({ id: "a1", role: "assistant", content: "" }),  // earlier empty — keep
      msg({ id: "a2", role: "assistant", content: "Done" }),
    ];
    expect(dropEmptyTrailingBubble(messages)).toEqual(messages);
  });

  it("keeps a trailing user message even when empty", () => {
    // User messages aren't bubbles produced by streaming — they're typed.
    // (Empty user messages are blocked at the input level anyway, but
    // dropEmptyTrailingBubble is scoped to assistant bubbles only.)
    const messages = [msg({ id: "u1", role: "user", content: "" })];
    expect(dropEmptyTrailingBubble(messages)).toEqual(messages);
  });

  it("returns the same reference when no change is needed", () => {
    const messages = [msg({ id: "u1", role: "user", content: "hi" })];
    expect(dropEmptyTrailingBubble(messages)).toBe(messages);
  });

  it("handles empty input", () => {
    const messages: ForgeMessage[] = [];
    expect(dropEmptyTrailingBubble(messages)).toBe(messages);
  });
});
