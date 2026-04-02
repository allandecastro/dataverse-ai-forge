"use client";

import { SidebarHeader } from "./SidebarHeader";
import { SolutionBadge } from "./SolutionBadge";
import { SidebarFooter } from "./SidebarFooter";
import { demoEnvironment } from "@/lib/demo-data";

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

      {/* Spacer to push footer to bottom */}
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
