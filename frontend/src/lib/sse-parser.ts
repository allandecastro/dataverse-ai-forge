import type { SSEEvent } from "./sse-types";

const VALID_EVENT_TYPES = new Set([
  "RUN_STARTED",
  "STATE_DELTA",
  "TEXT_MESSAGE_START",
  "TEXT_MESSAGE_CONTENT",
  "TEXT_MESSAGE_END",
  "TOOL_CALL_START",
  "TOOL_CALL_ARGS",
  "TOOL_CALL_END",
  "TOOL_CALL_RESULT",
  "RUN_FINISHED",
]);

/**
 * Parse an SSE stream from a fetch Response into typed events.
 * Handles partial chunks and multi-line SSE data fields.
 *
 * <p>Pass <code>signal</code> to bound mid-parse work. The fetch-level abort
 * already aborts <code>reader.read()</code>, but if a single chunk lands with
 * a long burst of events the per-event yield loop won't notice an abort that
 * fires DURING the burst. Checking <code>signal.aborted</code> between events
 * gives the caller a hard cap on how many post-abort events can leak through
 * (≤ 1, instead of "whatever was in the buffered chunk").</p>
 */
export async function* parseSSEStream(
  response: Response,
  signal?: AbortSignal,
): AsyncGenerator<SSEEvent> {
  const body = response.body;
  if (!body) {
    throw new Error("SSE stream body is null — backend may be unreachable");
  }

  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";

  try {
    while (true) {
      // Abort can fire while we're awaiting reader.read() (which itself
      // throws on abort) OR between iterations. Belt-and-braces check.
      if (signal?.aborted) break;

      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      // SSE events are separated by double newlines
      const parts = buffer.split("\n\n");
      // Last part may be incomplete — keep it in buffer
      buffer = parts.pop() ?? "";

      for (const part of parts) {
        // Bounded per-event abort check — caps post-abort leakage at ≤1
        // event even when a single read() returns a chunk packed with them.
        if (signal?.aborted) return;

        const trimmed = part.trim();
        if (!trimmed) continue;

        // Extract data from "data: {json}" lines
        let data = "";
        for (const line of trimmed.split("\n")) {
          if (line.startsWith("data: ")) {
            data += line.slice(6);
          } else if (line.startsWith("data:")) {
            data += line.slice(5);
          }
        }

        if (!data) continue;

        try {
          const parsed = JSON.parse(data);
          // Validate event type before yielding
          if (parsed && typeof parsed.type === "string" && VALID_EVENT_TYPES.has(parsed.type)) {
            yield parsed as SSEEvent;
          } else if (process.env.NODE_ENV === "development") {
            console.warn("[sse-parser] Unknown event type:", parsed?.type, data.slice(0, 200));
          }
        } catch {
          if (process.env.NODE_ENV === "development") {
            console.warn("[sse-parser] Malformed JSON in SSE event:", data.slice(0, 200));
          }
        }
      }
    }
  } finally {
    reader.releaseLock();
  }
}
