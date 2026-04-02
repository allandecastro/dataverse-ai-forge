"use client";

import { Timer, Activity, Bug } from "lucide-react";
import { SidebarHeader } from "./SidebarHeader";
import { SolutionBadge } from "./SolutionBadge";
import { SidebarFooter } from "./SidebarFooter";
import { Pill } from "@/components/primitives/Pill";
import { demoEnvironment } from "@/lib/demo-data";

export type PanelId = "timeline" | "activity";

interface SidebarProps {
  collapsed: boolean;
  onCollapsedChange: (collapsed: boolean) => void;
  tokenCount: number;
  cost: number;
  solutionName?: string;
  solutionPrefix?: string;
  solutionVersion?: string;
  onTokenPillClick?: () => void;
  projectName?: string;
  onSwitchProject?: () => void;
  onSettingsClick?: () => void;
  activePanel: PanelId | null;
  onTogglePanel: (id: PanelId) => void;
  activityCount?: number;
  diagOpen?: boolean;
  onToggleDiag?: () => void;
}

export function Sidebar({
  collapsed,
  onCollapsedChange,
  tokenCount,
  cost,
  solutionName,
  solutionPrefix,
  solutionVersion,
  onTokenPillClick,
  projectName,
  onSwitchProject,
  onSettingsClick,
  activePanel,
  onTogglePanel,
  activityCount = 0,
  diagOpen,
  onToggleDiag,
}: SidebarProps) {
  return (
    <aside
      className="flex-shrink-0 flex flex-col h-full border-r border-border bg-bg-1 transition-all duration-300 overflow-hidden relative z-10"
      style={{ width: collapsed ? 52 : 220 }}
    >
      <SidebarHeader collapsed={collapsed} projectName={projectName} onSwitchProject={onSwitchProject} />

      <SolutionBadge
        collapsed={collapsed}
        displayName={solutionName || "New Solution"}
        prefix={solutionPrefix || "—"}
        version={solutionVersion || "0.0.0.0"}
      />

      {/* Panel toggles */}
      <nav className="flex flex-col gap-0.5 px-2 py-1">
        <button
          onClick={() => onTogglePanel("timeline")}
          title={collapsed ? "Timeline" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            activePanel === "timeline"
              ? "bg-bg-3 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <Timer size={14} className="flex-shrink-0" />
          {!collapsed && <span className="flex-1 text-left">Timeline</span>}
        </button>
        <button
          onClick={() => onTogglePanel("activity")}
          title={collapsed ? "Activity Log" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            activePanel === "activity"
              ? "bg-bg-3 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <Activity size={14} className="flex-shrink-0" />
          {!collapsed && (
            <>
              <span className="flex-1 text-left">Activity</span>
              {activityCount > 0 && (
                <Pill label={String(activityCount)} color="neutral" />
              )}
            </>
          )}
        </button>
        <button
          onClick={onToggleDiag}
          title={collapsed ? "Diagnostics" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            diagOpen
              ? "bg-bg-3 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <Bug size={14} className="flex-shrink-0" />
          {!collapsed && <span className="flex-1 text-left">Diagnostics</span>}
        </button>
      </nav>

      <div className="flex-1" />

      <SidebarFooter
        collapsed={collapsed}
        environment={demoEnvironment}
        tokenCount={tokenCount}
        cost={cost}
        onToggleCollapse={() => onCollapsedChange(!collapsed)}
        onTokenPillClick={onTokenPillClick}
        onSettingsClick={onSettingsClick}
      />
    </aside>
  );
}
