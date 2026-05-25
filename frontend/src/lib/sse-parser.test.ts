import { describe, it, expect } from "vitest";
import { parseSSEStream } from "./sse-parser";
import type { SSEEvent } from "./sse-types";

/**
 * Mint a Response whose body is a ReadableStream of UTF-8-encoded chunks.
 * Mirrors what fetch returns; lets us drive parseSSEStream without a server.
 */
function responseFrom(chunks: string[]): Response {
  const encoder = new TextEncoder();
  let i = 0;
  const stream = new ReadableStream<Uint8Array>({
    pull(controller) {
      if (i >= chunks.length) {
        controller.close();
        return;
      }
      controller.enqueue(encoder.encode(chunks[i++]));
    },
  });
  return new Response(stream, { headers: { "Content-Type": "text/event-stream" } });
}

function sseEvent(type: string, extra: Record<string, unknown> = {}): string {
  return `data: ${JSON.stringify({ type, ...extra })}\n\n`;
}

describe("parseSSEStream — happy path", () => {
  it("yields a single well-formed event", async () => {
    const res = responseFrom([sseEvent("RUN_STARTED", { threadId: "t1" })]);
    const events: SSEEvent[] = [];
    for await (const ev of parseSSEStream(res)) events.push(ev);
    expect(events).toHaveLength(1);
    expect(events[0].type).toBe("RUN_STARTED");
  });

  it("yields events split across multiple chunks (partial-buffer handling)", async () => {
    // The TEXT_MESSAGE_START SSE is split mid-JSON across two chunks.
    const ev = sseEvent("TEXT_MESSAGE_START", { messageId: "m1", agent: "SA" });
    const splitAt = Math.floor(ev.length / 2);
    const res = responseFrom([ev.slice(0, splitAt), ev.slice(splitAt)]);
    const events: SSEEvent[] = [];
    for await (const x of parseSSEStream(res)) events.push(x);
    expect(events).toHaveLength(1);
    expect(events[0].type).toBe("TEXT_MESSAGE_START");
  });

  it("ignores malformed JSON between valid events without dropping the stream", async () => {
    const res = responseFrom([
      sseEvent("RUN_STARTED", { threadId: "t1" }),
      "data: {not json\n\n",
      sseEvent("RUN_FINISHED", { threadId: "t1" }),
    ]);
    const events: SSEEvent[] = [];
    for await (const ev of parseSSEStream(res)) events.push(ev);
    expect(events.map(e => e.type)).toEqual(["RUN_STARTED", "RUN_FINISHED"]);
  });

  it("skips unknown event types instead of yielding them", async () => {
    const res = responseFrom([
      sseEvent("RUN_STARTED"),
      sseEvent("BOGUS_EVENT", { x: 1 }),
      sseEvent("RUN_FINISHED"),
    ]);
    const types: string[] = [];
    for await (const ev of parseSSEStream(res)) types.push(ev.type);
    expect(types).toEqual(["RUN_STARTED", "RUN_FINISHED"]);
  });
});

describe("parseSSEStream — abort handling", () => {
  it("stops early when the signal is already aborted at start", async () => {
    const res = responseFrom([
      sseEvent("RUN_STARTED"),
      sseEvent("RUN_FINISHED"),
    ]);
    const controller = new AbortController();
    controller.abort();
    const events: SSEEvent[] = [];
    for await (const ev of parseSSEStream(res, controller.signal)) events.push(ev);
    // No events should leak — the signal is already aborted before the
    // first iteration of the while loop.
    expect(events).toHaveLength(0);
  });

  it("caps post-abort event leakage at ≤1 when abort fires mid-burst", async () => {
    // Pack three events into a single chunk so they all arrive in one read().
    // Without the per-event signal check, all three would yield before the
    // outer while loop got another chance to notice the abort.
    const burst =
      sseEvent("RUN_STARTED") +
      sseEvent("TEXT_MESSAGE_START", { messageId: "m1", agent: "SA" }) +
      sseEvent("RUN_FINISHED");
    const res = responseFrom([burst]);

    const controller = new AbortController();
    const events: SSEEvent[] = [];
    for await (const ev of parseSSEStream(res, controller.signal)) {
      events.push(ev);
      // Caller aborts immediately after the first event. The per-event
      // signal check inside the parser should stop the burst.
      controller.abort();
    }
    expect(events).toHaveLength(1);
    expect(events[0].type).toBe("RUN_STARTED");
  });

  it("works without a signal (backward compatibility)", async () => {
    // The signal arg is optional — callers that don't care still get the
    // full event stream (the original behaviour pre-fix).
    const res = responseFrom([
      sseEvent("RUN_STARTED"),
      sseEvent("RUN_FINISHED"),
    ]);
    const events: SSEEvent[] = [];
    for await (const ev of parseSSEStream(res)) events.push(ev);
    expect(events).toHaveLength(2);
  });
});
