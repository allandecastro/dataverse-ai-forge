"use client";

import { useEffect } from "react";

/**
 * Polls backend health on mount and every `intervalMs`, skipping work while the
 * tab is hidden and forcing a fresh fetch the moment it becomes visible again.
 * Extracted from WorkspaceContent so the page component stays focused on layout.
 */
export function useBackendHealthPolling(fetchHealth: () => void | Promise<void>, intervalMs = 30_000) {
  useEffect(() => {
    void fetchHealth();
    const interval = setInterval(() => {
      if (document.visibilityState === "hidden") return;
      void fetchHealth();
    }, intervalMs);
    const onVisible = () => {
      if (document.visibilityState === "visible") void fetchHealth();
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      clearInterval(interval);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [fetchHealth, intervalMs]);
}
