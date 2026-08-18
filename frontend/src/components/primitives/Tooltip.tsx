"use client";

import type { ReactNode } from "react";

interface TooltipProps {
  /** The text shown on hover. When empty, the wrapper is transparent (no tooltip). */
  label?: string;
  /** Which side of the trigger the bubble appears on. Default "bottom" suits
   *  top-of-screen controls; use "top" for anything near the bottom edge. */
  side?: "top" | "bottom";
  children: ReactNode;
}

/**
 * Lightweight styled tooltip. CSS-only (hover-driven, no JS state) so it stays
 * cheap and works during streaming re-renders. The trigger should carry its own
 * accessible name (aria-label); this bubble is decorative (aria-hidden).
 *
 * Uses a NAMED group (`group/tooltip`) so it never triggers on the many other
 * `group` hover scopes in the app (message rows, diagram cards, …).
 */
export function Tooltip({ label, side = "bottom", children }: TooltipProps) {
  if (!label) return <>{children}</>;

  const pos = side === "top" ? "bottom-full mb-1.5" : "top-full mt-1.5";

  return (
    <span className="relative inline-flex group/tooltip">
      {children}
      <span
        role="tooltip"
        aria-hidden="true"
        className={`pointer-events-none absolute left-1/2 -translate-x-1/2 ${pos} z-50 whitespace-nowrap rounded border border-border-hi bg-bg-3 px-2 py-1 text-t-1 opacity-0 shadow-md transition-opacity duration-100 group-hover/tooltip:opacity-100`}
        style={{ fontSize: 11 }}
      >
        {label}
      </span>
    </span>
  );
}
