"use client";

import type { LucideIcon } from "lucide-react";
import { Tooltip } from "./Tooltip";

interface IconBtnProps {
  icon: LucideIcon;
  onClick?: () => void;
  active?: boolean;
  /** Doubles as the styled tooltip label AND the button's accessible name. */
  title?: string;
  /** Tooltip side. Default "bottom"; pass "top" for buttons near the bottom edge. */
  tooltipSide?: "top" | "bottom";
}

export function IconBtn({ icon: Icon, onClick, active, title, tooltipSide = "bottom" }: IconBtnProps) {
  return (
    <Tooltip label={title} side={tooltipSide}>
      <button
        onClick={onClick}
        aria-label={title}
        className={`flex items-center justify-center rounded border transition-colors ${
          active
            ? "border-border-hi bg-bg-3 text-t-1"
            : "border-transparent text-t-3 hover:border-border hover:bg-bg-2 hover:text-t-2"
        }`}
        style={{ width: 28, height: 28 }}
      >
        <Icon size={14} />
      </button>
    </Tooltip>
  );
}
