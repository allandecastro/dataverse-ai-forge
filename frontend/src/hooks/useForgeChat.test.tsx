import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { act, cleanup, renderHook, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { ForgeChatProvider, useForgeChatContext } from "./useForgeChat";
import { STORAGE_CHAT_MESSAGES } from "@/lib/constants";

// ── SSE stream helpers ──────────────────────────────────────────────────────
// useForgeChat only touches response.ok / response.status / response.body, so we
// duck-type the fetch result rather than constructing a real Response (keeps the
// test independent of the test environment's Response/stream implementation).

const SESSION = "test-session";
const RUN = "run-1";
const THREAD = SESSION;

function encodeEvents(events: object[]): Uint8Array[] {
  const encoder = new TextEncoder();
  return events.map((e) => encoder.encode(`data: ${JSON.stringify(e)}\n\n`));
}

function streamFrom(events: object[]): ReadableStream<Uint8Array> {
  const chunks = encodeEvents(events);
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const c of chunks) controller.enqueue(c);
      controller.close();
    },
  });
}

function okStream(events: object[]) {
  return { ok: true, status: 200, body: streamFrom(events) };
}

// A stream that delivers its events but stays OPEN until `gate` resolves — lets a
// test hold `isStreaming` true while it exercises concurrent behaviour.
function gatedStream(events: object[], gate: Promise<void>): ReadableStream<Uint8Array> {
  const chunks = encodeEvents(events);
  return new ReadableStream<Uint8Array>({
    async start(controller) {
      for (const c of chunks) controller.enqueue(c);
      await gate;
      controller.close();
    },
  });
}

// A stream that delivers its events then ERRORS mid-flight — models a dropped
// connection after the backend has already emitted some events. Uses pull() so
// each chunk is actually read before the error: erroring in start() would reset
// the queue and drop the not-yet-read chunks (per the WHATWG streams spec).
function erroringStream(events: object[]): ReadableStream<Uint8Array> {
  const chunks = encodeEvents(events);
  let i = 0;
  return new ReadableStream<Uint8Array>({
    pull(controller) {
      if (i < chunks.length) {
        controller.enqueue(chunks[i++]);
      } else {
        controller.error(new Error("stream dropped"));
      }
    },
  });
}

// A turn where the agent emits a request_approval tool call (no RUN_FINISHED).
function approvalTurn() {
  return [
    { type: "RUN_STARTED", threadId: THREAD, runId: RUN },
    { type: "TOOL_CALL_START", threadId: THREAD, runId: RUN, toolCallId: "t1", toolCallName: "RequestApproval", agentName: "DataModelBuilder" },
    { type: "TOOL_CALL_ARGS", threadId: THREAD, runId: RUN, toolCallId: "t1", delta: JSON.stringify({ operationType: "create_table", description: "Create adc_phone", riskLevel: "orange", details: "schema=adc_phone" }) },
    { type: "TOOL_CALL_RESULT", threadId: THREAD, runId: RUN, toolCallId: "t1", result: '"Approval requested"' },
  ];
}

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((r) => {
    resolve = r;
  });
  return { promise, resolve };
}

// A minimal "assistant says `text`" turn: open bubble, stream content, close, finish.
function assistantTurn(text: string, messageId = "m1") {
  return [
    { type: "RUN_STARTED", threadId: THREAD, runId: RUN },
    { type: "TEXT_MESSAGE_START", threadId: THREAD, runId: RUN, messageId, role: "assistant" },
    { type: "TEXT_MESSAGE_CONTENT", threadId: THREAD, runId: RUN, messageId, delta: text },
    { type: "TEXT_MESSAGE_END", threadId: THREAD, runId: RUN, messageId },
    { type: "RUN_FINISHED", threadId: THREAD, runId: RUN },
  ];
}

function wrapper({ children }: { children: ReactNode }) {
  return <ForgeChatProvider sessionId={SESSION}>{children}</ForgeChatProvider>;
}

function setFetch(impl: (...args: unknown[]) => unknown) {
  vi.stubGlobal("fetch", vi.fn(impl));
}

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("ForgeChatProvider — sendMessage", () => {
  it("appends the user message and streams the assistant reply", async () => {
    setFetch(() => Promise.resolve(okStream(assistantTurn("Hello from the agent"))));

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });

    await act(async () => {
      await result.current.sendMessage("Build me a table");
    });

    const roles = result.current.messages.map((m) => m.role);
    expect(roles).toEqual(["user", "assistant"]);
    expect(result.current.messages[0].content).toBe("Build me a table");
    expect(result.current.messages[1].content).toContain("Hello from the agent");
    expect(result.current.isStreaming).toBe(false);
  });

  it("POSTs to /api/chat with the session threadId and message history", async () => {
    let captured: { url: unknown; body: unknown } | null = null;
    setFetch((url: unknown, init: unknown) => {
      captured = { url, body: JSON.parse((init as RequestInit).body as string) };
      return Promise.resolve(okStream(assistantTurn("ok")));
    });

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("hi");
    });

    expect(captured!.url).toBe("/api/chat");
    const body = captured!.body as { threadId: string; messages: { role: string; content: string }[] };
    expect(body.threadId).toBe(SESSION);
    expect(body.messages.at(-1)).toEqual({ role: "user", content: "hi" });
  });

  it("persists messages to localStorage under the session key", async () => {
    setFetch(() => Promise.resolve(okStream(assistantTurn("persisted reply"))));

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("remember this");
    });

    await waitFor(() => {
      const raw = localStorage.getItem(STORAGE_CHAT_MESSAGES(SESSION));
      expect(raw).toBeTruthy();
      expect(raw).toContain("remember this");
    });
  });

  it("ignores a second send issued while a stream is in flight", async () => {
    const gate = deferred();
    const fetchMock = vi.fn(() =>
      Promise.resolve({ ok: true, status: 200, body: gatedStream(assistantTurn("first"), gate.promise) }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });

    // Start a turn but keep its stream open so isStreaming stays true.
    let inFlight!: Promise<void>;
    act(() => {
      inFlight = result.current.sendMessage("one");
    });
    await waitFor(() => expect(result.current.isStreaming).toBe(true));

    // A second send now (latest callback, isStreaming === true) must bail before fetch.
    await act(async () => {
      await result.current.sendMessage("two");
    });
    expect(fetchMock).toHaveBeenCalledTimes(1);

    // Release the first stream and let it settle cleanly.
    gate.resolve();
    await act(async () => {
      await inFlight;
    });
    expect(result.current.isStreaming).toBe(false);
  });
});

describe("ForgeChatProvider — error handling", () => {
  it("surfaces an error bubble when the backend responds non-OK", async () => {
    setFetch(() => Promise.resolve({ ok: false, status: 500, body: null }));

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("trigger 500");
    });

    const last = result.current.messages.at(-1)!;
    expect(last.role).toBe("assistant");
    expect(last.isError).toBe(true);
    expect(last.content).toContain("500");
    expect(result.current.isStreaming).toBe(false);
  });

  it("surfaces an error bubble on a network failure", async () => {
    setFetch(() => Promise.reject(new Error("Failed to fetch")));

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("offline");
    });

    const last = result.current.messages.at(-1)!;
    expect(last.isError).toBe(true);
    expect(last.content).toContain("Failed to fetch");
  });

  it("does NOT add an error bubble when the fetch is aborted intentionally", async () => {
    setFetch(() => Promise.reject(new DOMException("aborted", "AbortError")));

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("will be aborted");
    });

    // Only the user message remains; no "Connection error" assistant bubble.
    expect(result.current.messages.map((m) => m.role)).toEqual(["user"]);
    expect(result.current.messages.some((m) => m.isError)).toBe(false);
  });

  it("surfaces a parsed approval even when the stream errors before completing", async () => {
    setFetch(() => Promise.resolve({ ok: true, status: 200, body: erroringStream(approvalTurn()) }));

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("create a table");
    });

    // The connection dropped AFTER the approval was emitted — the card must still
    // surface (approving resubmits a fresh request, so the dead stream doesn't
    // block recovery), and the error is still reported.
    expect(result.current.pendingApproval).not.toBeNull();
    expect(result.current.pendingApproval).toMatchObject({
      operationType: "create_table",
      riskLevel: "orange",
    });
    expect(result.current.messages.some((m) => m.isError)).toBe(true);
  });
});

describe("ForgeChatProvider — session controls", () => {
  it("clearSession wipes messages and the persisted localStorage entry", async () => {
    setFetch(() => Promise.resolve(okStream(assistantTurn("to be cleared"))));

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("hello");
    });
    expect(result.current.messages.length).toBeGreaterThan(0);

    act(() => {
      result.current.clearSession();
    });

    expect(result.current.messages).toEqual([]);
    expect(localStorage.getItem(STORAGE_CHAT_MESSAGES(SESSION))).toBeNull();
  });

  it("retryLastMessage re-sends the last user message after a failure", async () => {
    // First attempt fails, second succeeds — retry should drop the failed
    // attempt's bubbles and produce a clean user+assistant pair.
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce({ ok: false, status: 503, body: null })
      .mockResolvedValueOnce(okStream(assistantTurn("recovered")));
    vi.stubGlobal("fetch", fetchMock);

    const { result } = renderHook(() => useForgeChatContext(), { wrapper });
    await act(async () => {
      await result.current.sendMessage("flaky request");
    });
    expect(result.current.messages.some((m) => m.isError)).toBe(true);

    await act(async () => {
      await result.current.retryLastMessage();
    });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const roles = result.current.messages.map((m) => m.role);
    expect(roles).toEqual(["user", "assistant"]);
    expect(result.current.messages.some((m) => m.isError)).toBe(false);
    expect(result.current.messages.at(-1)!.content).toContain("recovered");
  });
});
