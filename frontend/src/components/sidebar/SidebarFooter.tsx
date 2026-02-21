"use client";

import { Sun, Moon, Zap, ChevronLeft, ChevronRight } from "lucide-react";
import { IconBtn } from "@/components/primitives/IconBtn";
import { useTheme } from "@/components/layout/ThemeProvider";
import type { DemoEnvironment } from "@/lib/demo-data";

interface SidebarFooterProps {
  collapsed: boolean;
  environment: DemoEnvironment;
  tokenCount: number;
  cost: number;
  onToggleCollapse: () => void;
  onTokenPillClick?: () => void;
}

function formatTokens(n: number): string {
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`;
  return String(n);
}

function formatCost(n: number): string {
  return `$${n.toFixed(2)}`;
}

export function SidebarFooter({
  collapsed,
  environment,
  tokenCount,
  cost,
  onToggleCollapse,
  onTokenPillClick,
}: SidebarFooterProps) {
  const { mode, toggleTheme } = useTheme();

  if (collapsed) {
    return (
      <div className="flex flex-col items-center gap-2 border-t border-border py-2">
        <div
          className="rounded-full flex-shrink-0"
          style={{ width: 8, height: 8, backgroundColor: "var(--green)" }}
          title={`${environment.name} (${environment.type})`}
        />
        <IconBtn
          icon={mode === "dark" ? Sun : Moon}
          onClick={toggleTheme}
          title="Toggle theme"
        />
        <IconBtn
          icon={ChevronRight}
          onClick={onToggleCollapse}
          title="Expand sidebar"
        />
      </div>
    );
  }

  return (
    <div className="border-t border-border px-3 py-2 space-y-2">
      {/* Environment row */}
      <div className="flex items-center gap-2">
        <div
          className="rounded-full flex-shrink-0"
          style={{ width: 8, height: 8, backgroundColor: "var(--green)" }}
        />
        <span className="text-t-2 text-xs truncate flex-1">
          {environment.name}
        </span>
        <span className="text-t-4" style={{ fontSize: 10 }}>
          {environment.type}
        </span>
      </div>

      {/* Token + controls row */}
      <div className="flex items-center gap-2">
        {/* Token pill */}
        <button
          onClick={onTokenPillClick}
          className="flex items-center gap-1 rounded-full bg-bg-2 px-2 py-0.5 text-t-3 flex-1 min-w-0 hover:bg-bg-3 transition-colors cursor-pointer"
          style={{ fontSize: 11, fontFamily: "var(--font-geist-mono), monospace" }}
          title="View token usage breakdown"
        >
          <Zap size={11} className="flex-shrink-0" />
          <span className="truncate">
            {formatTokens(tokenCount)} · {formatCost(cost)}
          </span>
        </button>

        <IconBtn
          icon={mode === "dark" ? Sun : Moon}
          onClick={toggleTheme}
          title="Toggle theme"
        />
        <IconBtn
          icon={ChevronLeft}
          onClick={onToggleCollapse}
          title="Collapse sidebar"
        />
      </div>
    </div>
  );
}
