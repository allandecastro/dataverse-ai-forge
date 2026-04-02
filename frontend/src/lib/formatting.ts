/** Format timestamp as HH:MM:SS */
export function formatTime(ts: number): string {
  return new Date(ts).toLocaleTimeString("en-US", {
    hour12: false,
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

/** Format duration in ms to human-readable string */
export function formatDuration(ms: number | null): string {
  if (ms === null) return "...";
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

/** Format elapsed time with minutes support */
export function formatElapsed(ms: number): string {
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  const mins = Math.floor(ms / 60_000);
  const secs = ((ms % 60_000) / 1000).toFixed(0);
  return `${mins}m ${secs}s`;
}

/** Format cost as dollars */
export function formatCost(n: number): string {
  return `$${n.toFixed(3)}`;
}

/** Format token count (e.g., 4.2k, 1.3M) */
export function formatTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`;
  return String(n);
}

/** Duration pill color based on speed */
export function durationColor(ms: number | null): "green" | "orange" | "neutral" {
  if (ms === null) return "neutral";
  return ms <= 1000 ? "green" : "orange";
}
