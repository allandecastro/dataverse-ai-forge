/**
 * Shared JSON parsing helpers. Tool-call `args`/`result` arrive as strings that
 * are *usually* JSON but occasionally plain text (errors, free-form output), so
 * a throwing JSON.parse is never what callers want. These two helpers replace
 * the three near-identical `try { JSON.parse } catch` copies that had drifted
 * across ChatPanel, useDiagnostics, and ToolCallsInline.
 */

/** Parse JSON, falling back to the original string when it isn't valid JSON. */
export function safeParse(str: string): unknown {
  try {
    return JSON.parse(str);
  } catch {
    return str;
  }
}

/**
 * Pretty-print a JSON string with 2-space indent, returning the raw string
 * unchanged when it isn't valid JSON (or is itself a bare string).
 */
export function formatJson(str: string): string {
  const parsed = safeParse(str);
  // safeParse returns the *same* string reference only when parsing failed —
  // in that case echo the raw input. Otherwise re-stringify (this matches the
  // original behaviour, including quoting a bare JSON string like `"hi"`).
  return parsed === str ? str : JSON.stringify(parsed, null, 2);
}
