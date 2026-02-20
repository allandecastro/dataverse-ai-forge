"use client";

import { Sparkles } from "lucide-react";

interface SidebarHeaderProps {
  collapsed: boolean;
}

export function SidebarHeader({ collapsed }: SidebarHeaderProps) {
  return (
    <div className="flex h-12 items-center gap-2.5 border-b border-border px-3">
      <div
        className="flex items-center justify-center rounded-md flex-shrink-0"
        style={{
          width: 28,
          height: 28,
          background: "linear-gradient(135deg, var(--blue), var(--purple))",
        }}
      >
        <Sparkles size={14} className="text-white" />
      </div>
      {!collapsed && (
        <div className="flex flex-col min-w-0">
          <span className="text-t-1 font-semibold truncate" style={{ fontSize: 13 }}>
            Dataverse AI Forge
          </span>
          <span className="text-t-3 truncate" style={{ fontSize: 11 }}>
            Dataverse Builder
          </span>
        </div>
      )}
    </div>
  );
}
