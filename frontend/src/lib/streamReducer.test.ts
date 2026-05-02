import { describe, it, expect } from "vitest";
import { initStreamState, streamReducer, type StreamState } from "./streamReducer";
import type { SSEEvent } from "./sse-types";

const RUN_ID = "run-1";
const THREAD_ID = "t-1";

function fresh(initialAgentName = "SolutionArchitect"): StreamState {
  return initStreamState({
    messages: [],
    toolCalls: [],
    agentState: { activeAgent: initialAgentName },
    pendingApproval: null,
    lastUsage: null,
    initialAgentName,
  });
}

function ev(partial: SSEEvent): SSEEvent {
  return partial;
}

describe("streamReducer — STATE_DELTA", () => {
  it("updates activeAgent and currentAgentName from /activeAgent patch", () => {
    const next = streamReducer(
      fresh(),
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/activeAgent", value: "DataModelBuilder" }],
      }),
      100,
    );
    expect(next.agentState.activeAgent).toBe("DataModelBuilder");
    expect(next.currentAgentName).toBe("DataModelBuilder");
  });

  it("merges /reasoning into agentState without losing activeAgent", () => {
    const next = streamReducer(
      fresh("DataModelBuilder"),
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/reasoning", value: "Planning the schema..." }],
      }),
      100,
    );
    expect(next.agentState.reasoning).toBe("Planning the schema...");
    expect(next.agentState.activeAgent).toBe("DataModelBuilder");
  });

  it("applies /suggestedReplies as a string array on agentState", () => {
    const next = streamReducer(
      fresh(),
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/suggestedReplies", value: ["Yes, proceed", "Show me the plan"] }],
      }),
      100,
    );
    expect(next.agentState.suggestedReplies).toEqual(["Yes, proceed", "Show me the plan"]);
  });

  it("clears /suggestedReplies when an empty array arrives (RUN_STARTED reset)", () => {
    let s = streamReducer(
      fresh(),
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/suggestedReplies", value: ["Stale"] }],
      }),
      100,
    );
    s = streamReducer(
      s,
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/suggestedReplies", value: [] }],
      }),
      200,
    );
    expect(s.agentState.suggestedReplies).toEqual([]);
  });

  it("filters non-string entries from /suggestedReplies defensively", () => {
    const next = streamReducer(
      fresh(),
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/suggestedReplies", value: ["ok", 42, null, "go"] }],
      }),
      100,
    );
    expect(next.agentState.suggestedReplies).toEqual(["ok", "go"]);
  });

  it("merges /solutionState into agentState without losing other fields", () => {
    // Symptom this protects: a partial STATE_DELTA used to overwrite the
    // entire agentState, blanking out solutionState in the sidebar.
    const start = streamReducer(
      fresh(),
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/solutionState", value: { solutionName: "Demo" } }],
      }),
      100,
    );

    const next = streamReducer(
      start,
      ev({
        type: "STATE_DELTA",
        threadId: THREAD_ID,
        runId: RUN_ID,
        delta: [{ op: "replace", path: "/activeAgent", value: "Tester" }],
      }),
      200,
    );
    expect(next.agentState.activeAgent).toBe("Tester");
    expect(next.agentState.solutionState).toEqual({ solutionName: "Demo" });
  });
});

describe("streamReducer — text bubble lifecycle", () => {
  it("opens a new bubble on TEXT_MESSAGE_START with server-attributed agent", () => {
    const next = streamReducer(
      fresh("SolutionArchitect"),
      ev({
        type: "TEXT_MESSAGE_START",
        threadId: THREAD_ID,
        runId: RUN_ID,
        messageId: "m1",
        role: "assistant",
        agentName: "DataModelBuilder",
      }),
      150,
    );
    expect(next.messages).toHaveLength(1);
    expect(next.messages[0]).toMatchObject({
      id: "m1",
      role: "assistant",
      content: "",
      agentName: "DataModelBuilder",
      timestamp: 150,
    });
    expect(next.activeMessageId).toBe("m1");
    expect(next.lastBubbleId).toBe("m1");
    expect(next.currentAgentName).toBe("DataModelBuilder");
  });

  it("falls back to currentAgentName when START omits agentName", () => {
    const next = streamReducer(
      fresh("Tester"),
      ev({
        type: "TEXT_MESSAGE_START",
        threadId: THREAD_ID,
        runId: RUN_ID,
        messageId: "m1",
        role: "assistant",
      }),
      150,
    );
    expect(next.messages[0].agentName).toBe("Tester");
  });

  it("appends content via TEXT_MESSAGE_CONTENT and accumulates the delta", () => {
    let s = streamReducer(
      fresh(),
      ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", role: "assistant" }),
      100,
    );
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", delta: "Hello " }), 110);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", delta: "world" }), 120);

    expect(s.messages[0].content).toBe("Hello world");
    expect(s.activeContent).toBe("Hello world");
  });

  it("opens a defensive bubble if CONTENT arrives without a prior START", () => {
    const s = streamReducer(
      fresh("DataModelBuilder"),
      ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "m99", delta: "orphan text" }),
      100,
    );
    expect(s.messages).toHaveLength(1);
    expect(s.messages[0]).toMatchObject({ id: "m99", content: "orphan text", agentName: "DataModelBuilder" });
  });

  it("drops empty assistant bubble at TEXT_MESSAGE_END (no content streamed)", () => {
    let s = streamReducer(
      fresh(),
      ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", role: "assistant" }),
      100,
    );
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1" }), 110);

    expect(s.messages).toHaveLength(0);
    expect(s.lastBubbleId).toBeNull();
  });

  it("keeps non-empty bubble at TEXT_MESSAGE_END", () => {
    let s = streamReducer(
      fresh(),
      ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", role: "assistant" }),
      100,
    );
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", delta: "Hello" }), 110);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1" }), 120);

    expect(s.messages).toHaveLength(1);
    expect(s.messages[0].content).toBe("Hello");
    expect(s.activeMessageId).toBeNull();
    // lastBubbleId survives — tool calls fired between text messages can still
    // attach to the bubble that spawned them.
    expect(s.lastBubbleId).toBe("m1");
  });

  it("preserves existing bubbles from prior turns when a new one opens", () => {
    // Simulates a chained handoff: SA bubble already exists, DMB opens its own.
    const start = initStreamState({
      messages: [{ id: "old", role: "assistant", content: "earlier", timestamp: 0, agentName: "SA" }],
      toolCalls: [],
      agentState: { activeAgent: "DataModelBuilder" },
      pendingApproval: null,
      lastUsage: null,
      initialAgentName: "DataModelBuilder",
    });
    const next = streamReducer(
      start,
      ev({
        type: "TEXT_MESSAGE_START",
        threadId: THREAD_ID,
        runId: RUN_ID,
        messageId: "new",
        role: "assistant",
        agentName: "DataModelBuilder",
      }),
      100,
    );
    expect(next.messages).toHaveLength(2);
    expect(next.messages[0].id).toBe("old");
    expect(next.messages[1].id).toBe("new");
  });
});

describe("streamReducer — tool calls", () => {
  it("attaches a TOOL_CALL_START to the active bubble when one is open and the agent matches", () => {
    let s = streamReducer(
      fresh("SolutionArchitect"),
      ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", role: "assistant", agentName: "SolutionArchitect" }),
      100,
    );
    s = streamReducer(
      s,
      ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "GetTablesList", agentName: "SolutionArchitect" }),
      110,
    );
    expect(s.toolCalls).toHaveLength(1);
    expect(s.toolCalls[0]).toMatchObject({
      id: "t1",
      name: "GetTablesList",
      messageId: "m1",
      agent: "SolutionArchitect",
      status: "running",
      startTime: 110,
    });
    // No placeholder spawned when the active bubble already belongs to this agent.
    expect(s.messages).toHaveLength(1);
  });

  it("attaches TOOL_CALL_START to lastBubbleId when no bubble is currently active", () => {
    // The chained-handoff case: bubble closed (END), then tool calls fire before
    // the next agent's bubble opens.
    let s = streamReducer(
      fresh(),
      ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", role: "assistant" }),
      100,
    );
    s = streamReducer(
      s,
      ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1", delta: "hi" }),
      105,
    );
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "m1" }), 110);
    s = streamReducer(
      s,
      ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "X" }),
      120,
    );
    expect(s.toolCalls[0].messageId).toBe("m1");
  });

  it("accumulates args via TOOL_CALL_ARGS and reflects them in toolCalls list", () => {
    let s = streamReducer(
      fresh(),
      ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "X" }),
      100,
    );
    s = streamReducer(s, ev({ type: "TOOL_CALL_ARGS", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", delta: '{"a":' }), 110);
    s = streamReducer(s, ev({ type: "TOOL_CALL_ARGS", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", delta: "1}" }), 120);
    expect(s.toolCalls[0].args).toBe('{"a":1}');
  });

  it("ignores TOOL_CALL_ARGS for an unknown toolCallId (no crash)", () => {
    const s = streamReducer(
      fresh(),
      ev({ type: "TOOL_CALL_ARGS", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "ghost", delta: "{}" }),
      100,
    );
    expect(s.toolCalls).toHaveLength(0);
  });

  it("marks tool complete and records endTime on TOOL_CALL_RESULT", () => {
    let s = streamReducer(
      fresh(),
      ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "X" }),
      100,
    );
    s = streamReducer(
      s,
      ev({ type: "TOOL_CALL_RESULT", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", result: '"Created table adc_phone"' }),
      200,
    );
    expect(s.toolCalls[0]).toMatchObject({ status: "complete", endTime: 200, result: '"Created table adc_phone"' });
  });

  it("marks tool error when result starts with ERROR (quote-wrapped)", () => {
    let s = streamReducer(
      fresh(),
      ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "X" }),
      100,
    );
    s = streamReducer(
      s,
      ev({ type: "TOOL_CALL_RESULT", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", result: '"ERROR: 401 Unauthorized"' }),
      200,
    );
    expect(s.toolCalls[0].status).toBe("error");
  });

  it("populates pendingApproval when a request_approval tool finishes", () => {
    let s = streamReducer(
      fresh(),
      ev({
        type: "TOOL_CALL_START",
        threadId: THREAD_ID,
        runId: RUN_ID,
        toolCallId: "t1",
        toolCallName: "RequestApproval",
        agentName: "DataModelBuilder",
      }),
      100,
    );
    s = streamReducer(
      s,
      ev({ type: "TOOL_CALL_ARGS", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", delta: JSON.stringify({
        operationType: "create_table",
        description: "Create adc_phone",
        riskLevel: "orange",
        details: "schema=adc_phone",
      }) }),
      110,
    );
    s = streamReducer(
      s,
      ev({ type: "TOOL_CALL_RESULT", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", result: '"Approval requested"' }),
      200,
    );

    expect(s.pendingApproval).toMatchObject({
      toolCallId: "t1",
      operationType: "create_table",
      riskLevel: "orange",
      agentName: "DataModelBuilder",
    });
  });
});

describe("streamReducer — RUN_FINISHED + lifecycle markers", () => {
  it("captures usage from RUN_FINISHED metadata", () => {
    const next = streamReducer(
      fresh(),
      ev({
        type: "RUN_FINISHED",
        threadId: THREAD_ID,
        runId: RUN_ID,
        metadata: { usage: { inputTokens: 100, outputTokens: 50, totalTokens: 150 } },
      }),
      300,
    );
    expect(next.lastUsage).toEqual({ inputTokens: 100, outputTokens: 50, totalTokens: 150 });
  });

  it("does not sweep messages on RUN_FINISHED — empty bubbles already dropped at END", () => {
    // Symptom this protects: SA's response disappearing mid-conversation because
    // a global RUN_FINISHED sweep fired against bubbles whose tool calls were
    // attached via the smart attributeToolCalls() rule (no messageId on the tc).
    const start = initStreamState({
      messages: [{ id: "m1", role: "assistant", content: "Hello", timestamp: 0, agentName: "SA" }],
      toolCalls: [],
      agentState: { activeAgent: "SA" },
      pendingApproval: null,
      lastUsage: null,
      initialAgentName: "SA",
    });
    const next = streamReducer(
      start,
      ev({ type: "RUN_FINISHED", threadId: THREAD_ID, runId: RUN_ID }),
      300,
    );
    expect(next.messages).toHaveLength(1);
    expect(next.messages[0].id).toBe("m1");
  });

  it("RUN_STARTED and TOOL_CALL_END are no-ops", () => {
    const start = fresh();
    const afterStarted = streamReducer(
      start,
      ev({ type: "RUN_STARTED", threadId: THREAD_ID, runId: RUN_ID }),
      0,
    );
    const afterToolEnd = streamReducer(
      start,
      ev({ type: "TOOL_CALL_END", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "anything" }),
      0,
    );
    expect(afterStarted).toBe(start);
    expect(afterToolEnd).toBe(start);
  });
});

describe("streamReducer — chained handoff scenario", () => {
  it("SA → DMB chain produces two distinct attributed bubbles + tool attached to DMB via placeholder", () => {
    let s = fresh("SolutionArchitect");

    // SA opens, writes, closes
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "sa1", role: "assistant", agentName: "SolutionArchitect" }), 100);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "sa1", delta: "Handing off to DMB" }), 110);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "sa1" }), 120);

    // STATE_DELTA flips the active agent
    s = streamReducer(s, ev({ type: "STATE_DELTA", threadId: THREAD_ID, runId: RUN_ID, delta: [{ op: "replace", path: "/activeAgent", value: "DataModelBuilder" }] }), 130);

    // DMB fires a tool BEFORE its own bubble opens — a placeholder bubble for DMB
    // is created so the tool doesn't visually land on SA's bubble during streaming.
    s = streamReducer(s, ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "PlanCreateTable", agentName: "DataModelBuilder" }), 135);

    // While the real bubble has not yet opened, the tool is attached to a placeholder
    // (id starts with `placeholder-`) and a 2nd assistant row exists for DMB.
    expect(s.messages).toHaveLength(2);
    expect(s.messages[1]).toMatchObject({ agentName: "DataModelBuilder", content: "" });
    expect(s.messages[1].id.startsWith("placeholder-")).toBe(true);
    expect(s.toolCalls[0].messageId).toBe(s.messages[1].id);

    // DMB then opens its own bubble — placeholder is recycled in place, tool's
    // messageId is rewritten to the server-issued id, no visual jump.
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "dmb1", role: "assistant", agentName: "DataModelBuilder" }), 140);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "dmb1", delta: "Plan ready" }), 150);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "dmb1" }), 160);

    expect(s.messages).toHaveLength(2);
    expect(s.messages[0]).toMatchObject({ id: "sa1", agentName: "SolutionArchitect", content: "Handing off to DMB" });
    expect(s.messages[1]).toMatchObject({ id: "dmb1", agentName: "DataModelBuilder", content: "Plan ready" });
    expect(s.toolCalls[0]).toMatchObject({ id: "t1", agent: "DataModelBuilder", messageId: "dmb1" });
  });

  it("same-agent new turn after user input creates a fresh placeholder for tool calls", () => {
    // Symptom this protects: DMB asks a question, user replies, DMB fires tools.
    // Without a placeholder the tools visually land on DMB's earlier (closed)
    // bubble, then jump to the new bubble once it opens.
    let s = fresh("DataModelBuilder");
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "d1", role: "assistant", agentName: "DataModelBuilder" }), 100);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "d1", delta: "Should I create the table?" }), 110);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "d1" }), 120);

    // After user answers "yes", DMB fires a tool before opening its next bubble.
    s = streamReducer(s, ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "PlanCreateTable", agentName: "DataModelBuilder" }), 200);

    expect(s.messages).toHaveLength(2);
    expect(s.messages[1]).toMatchObject({ agentName: "DataModelBuilder", content: "" });
    expect(s.messages[1].id.startsWith("placeholder-")).toBe(true);
    expect(s.toolCalls[0].messageId).toBe(s.messages[1].id);

    // New bubble opens; placeholder is recycled in place.
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "d2", role: "assistant", agentName: "DataModelBuilder" }), 210);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "d2", delta: "Done." }), 220);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "d2" }), 230);

    expect(s.messages).toHaveLength(2);
    expect(s.messages[1]).toMatchObject({ id: "d2", agentName: "DataModelBuilder", content: "Done." });
    expect(s.toolCalls[0].messageId).toBe("d2");
  });

  it("multiple tool calls before the real bubble opens all share one placeholder", () => {
    let s = fresh("SolutionArchitect");
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_START", threadId: THREAD_ID, runId: RUN_ID, messageId: "sa1", role: "assistant", agentName: "SolutionArchitect" }), 100);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_CONTENT", threadId: THREAD_ID, runId: RUN_ID, messageId: "sa1", delta: "Handing off" }), 110);
    s = streamReducer(s, ev({ type: "TEXT_MESSAGE_END", threadId: THREAD_ID, runId: RUN_ID, messageId: "sa1" }), 120);

    s = streamReducer(s, ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t1", toolCallName: "X", agentName: "DataModelBuilder" }), 130);
    s = streamReducer(s, ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t2", toolCallName: "Y", agentName: "DataModelBuilder" }), 140);
    s = streamReducer(s, ev({ type: "TOOL_CALL_START", threadId: THREAD_ID, runId: RUN_ID, toolCallId: "t3", toolCallName: "Z", agentName: "DataModelBuilder" }), 150);

    // Only ONE placeholder should be added, and all three tools should attach to it.
    expect(s.messages).toHaveLength(2);
    const placeholderId = s.messages[1].id;
    expect(placeholderId.startsWith("placeholder-")).toBe(true);
    expect(s.toolCalls.map((t) => t.messageId)).toEqual([placeholderId, placeholderId, placeholderId]);
  });
});
