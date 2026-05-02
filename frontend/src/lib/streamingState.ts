/**
 * Pure helpers for the bug-prone bits of useForgeChat's SSE state machine.
 * Lives outside the hook so the tricky string handling can be unit-tested
 * without spinning up DOM + streams.
 *
 * Past regressions these protect against:
 *   - "Error: 401" from a tool came through as result-success because the
 *     framework wraps results in JSON quotes (e.g. "\"Error: 401\"") so a
 *     naive `result.startsWith("ERROR")` missed it.
 *   - A chained agent emitted START/END with no content — leaving a blank
 *     assistant bubble in the chat.
 */

export type ToolCallStatus = "running" | "complete" | "error";

/**
 * Decide whether a finished TOOL_CALL_RESULT represents an error or a success.
 *
 * Tool results are JSON-encoded by the backend, so the raw string a string
 * result like `ERROR: foo` arrives as `"\"ERROR: foo\""`. We strip surrounding
 * quotes, then case-insensitively check for an `error` prefix — covers both our
 * `ERROR:` convention and the framework's `Error:` exception messages.
 */
export function classifyToolResultStatus(rawResult: string): ToolCallStatus {
  const unwrapped =
    rawResult.startsWith('"') && rawResult.endsWith('"')
      ? rawResult.slice(1, -1)
      : rawResult;
  return /^error[:\s]/i.test(unwrapped) ? "error" : "complete";
}

/**
 * At TEXT_MESSAGE_END, decide whether to keep an assistant bubble that has no
 * visible content. Returns false when the bubble is whitespace-only — the hook
 * drops empty bubbles immediately at close time so chained agents emitting only
 * tool calls don't leave a blank message in the chat.
 */
export function shouldKeepBubble(content: string): boolean {
  return content.trim().length > 0;
}
