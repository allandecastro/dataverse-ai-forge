import type { DiagnosticEntry } from "@/lib/types";

const DEDUP_WINDOW_MS = 2_000;

/**
 * Merge real-time diagnostic entries with historical audit entries.
 * Real-time entries take priority (richer data: payload, result, duration).
 * Deduplicates by matching toolName + timestamp within a 2s window.
 */
export function mergeEntries(
  realTimeEntries: DiagnosticEntry[],
  auditEntries: DiagnosticEntry[],
): DiagnosticEntry[] {
  // Build a quick-lookup set from real-time entries for deduplication
  const realTimeKeys = new Set(
    realTimeEntries.map((e) => `${e.toolName}:${e.timestamp}`),
  );

  const deduped = auditEntries.filter((audit) => {
    // Check if any real-time entry has the same tool within the dedup window
    for (const rt of realTimeEntries) {
      if (
        rt.toolName === audit.toolName &&
        Math.abs(rt.timestamp - audit.timestamp) <= DEDUP_WINDOW_MS
      ) {
        return false; // Duplicate — real-time version takes priority
      }
    }
    // Also skip exact matches
    if (realTimeKeys.has(`${audit.toolName}:${audit.timestamp}`)) {
      return false;
    }
    return true;
  });

  return [...realTimeEntries, ...deduped].sort(
    (a, b) => a.timestamp - b.timestamp,
  );
}
