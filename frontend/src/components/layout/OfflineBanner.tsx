"use client";

import { WifiOff, RefreshCw } from "lucide-react";

interface OfflineBannerProps {
  onRetry: () => void;
}

/**
 * Top-of-screen banner shown when the backend is unreachable. The polling
 * loop in page.tsx flips the trigger; this component is purely cosmetic.
 *
 * Surfaces the failure faster than waiting for the user's next action — a
 * "Failed to fetch" toast on Save was the previous-only signal a backend
 * crash happened.
 */
export function OfflineBanner({ onRetry }: OfflineBannerProps) {
  return (
    <div
      className="flex items-center justify-center gap-3 bg-red text-white px-4 py-1.5 text-xs"
      role="alert"
    >
      <WifiOff size={12} />
      <span>
        Backend disconnected. Agent calls and settings won&apos;t work until it&apos;s back.
      </span>
      <button
        onClick={onRetry}
        className="flex items-center gap-1 rounded px-2 py-0.5 bg-white/20 hover:bg-white/30 transition-colors"
        title="Retry the health check"
      >
        <RefreshCw size={11} />
        Retry
      </button>
    </div>
  );
}
