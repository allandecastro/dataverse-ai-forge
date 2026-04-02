"use client";

import { SidebarHeader } from "./SidebarHeader";
import { SidebarSearch } from "./SidebarSearch";
import { SidebarNav, type NavItemId } from "./SidebarNav";
import { SolutionBadge } from "./SolutionBadge";
import { SidebarFooter } from "./SidebarFooter";
import { demoSolution, demoEnvironment } from "@/lib/demo-data";

export type { NavItemId } from "./SidebarNav";

interface SidebarProps {
  collapsed: boolean;
  onCollapsedChange: (collapsed: boolean) => void;
  tokenCount: number;
  cost: number;
  solutionName?: string;
  solutionPrefix?: string;
  solutionVersion?: string;
  activeNav: NavItemId;
  onNavChange: (id: NavItemId) => void;
  activityCount: number;
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
  activeNav,
  onNavChange,
  activityCount,
  onTokenPillClick,
  projectName,
  onSwitchProject,
  onSettingsClick,
}: SidebarProps) {

  return (
    <aside
      className="flex-shrink-0 flex flex-col h-full border-r border-border bg-bg-1 transition-all duration-300 overflow-hidden relative z-10"
      style={{ width: collapsed ? 52 : 252 }}
    >
      <SidebarHeader collapsed={collapsed} projectName={projectName} onSwitchProject={onSwitchProject} />
      <SidebarSearch collapsed={collapsed} />
      <SidebarNav
        collapsed={collapsed}
        activeItem={activeNav}
        onItemClick={onNavChange}
        activityCount={activityCount}
      />

      <div className="border-t border-border-lt" />

      <SolutionBadge
        collapsed={collapsed}
        displayName={solutionName || demoSolution.displayName}
        prefix={solutionPrefix || demoSolution.prefix}
        version={solutionVersion || demoSolution.version}
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
