"use client";

import { Timer, Activity, Bug, Sparkles, Users } from "lucide-react";
import { SidebarHeader } from "./SidebarHeader";
import { SolutionBadge } from "./SolutionBadge";
import { SidebarFooter } from "./SidebarFooter";
import { Pill } from "@/components/primitives/Pill";
import { useSidebar } from "./SidebarContext";

export type { PanelId } from "./SidebarContext";

/**
 * Reads everything from <SidebarProvider>. Consumers should wrap their tree:
 *   <SidebarProvider value={...}><Sidebar /></SidebarProvider>
 *
 * Used to take 23 props; replaced with context to keep the component signature
 * stable as new sidebar widgets get added.
 */
export function Sidebar() {
  const {
    collapsed,
    onCollapsedChange,
    tokenCount,
    environmentName,
    environmentUrl,
    dataverseConnected,
    solutionName,
    solutionPrefix,
    solutionVersion,
    onTokenPillClick,
    projectName,
    onSwitchProject,
    onRenameProject,
    onSettingsClick,
    activePanel,
    onTogglePanel,
    activityCount = 0,
    diagOpen,
    onToggleDiag,
    hasNewToolCalls,
  } = useSidebar();

  return (
    <aside
      className="flex-shrink-0 flex flex-col h-full border-r border-border bg-bg-1 transition-all duration-300 overflow-hidden relative z-10"
      style={{ width: collapsed ? 52 : 220 }}
    >
      <SidebarHeader
        collapsed={collapsed}
        projectName={projectName}
        onSwitchProject={onSwitchProject}
        onRenameProject={onRenameProject}
      />

      <SolutionBadge
        collapsed={collapsed}
        displayName={solutionName || "New Solution"}
        prefix={solutionPrefix || "—"}
        version={solutionVersion || "0.0.0.0"}
      />

      {/* Panel toggles */}
      {!collapsed && (
        <div className="px-3 pt-3 pb-1 text-[10px] font-semibold uppercase tracking-[0.13em] text-t-4 font-mono">
          Workspace
        </div>
      )}
      <nav className="flex flex-col gap-0.5 px-2 py-1">
        {/* Assistant — default-active when no other panel is open. Clicking from
            Timeline/Activity returns the user to the chat view. Highlighted while
            activePanel is null so users know "where they are" by default. */}
        <button
          onClick={() => {
            if (activePanel !== null) onTogglePanel(activePanel);
          }}
          title={collapsed ? "Assistant" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            activePanel === null
              ? "bg-bg-2 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <Sparkles size={14} className={`flex-shrink-0 ${activePanel === null ? "text-accent-ink" : ""}`} />
          {!collapsed && <span className="flex-1 text-left">Assistant</span>}
        </button>
        <button
          onClick={() => onTogglePanel("timeline")}
          title={collapsed ? "Timeline" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            activePanel === "timeline"
              ? "bg-bg-2 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <div className="relative flex-shrink-0">
            <Timer size={14} className={activePanel === "timeline" ? "text-accent-ink" : ""} />
            {hasNewToolCalls && activePanel !== "timeline" && (
              <div className="absolute -top-0.5 -right-0.5 w-1.5 h-1.5 rounded-full bg-orange" />
            )}
          </div>
          {!collapsed && <span className="flex-1 text-left">Timeline</span>}
        </button>
        <button
          onClick={() => onTogglePanel("activity")}
          title={collapsed ? "Activity Log" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            activePanel === "activity"
              ? "bg-bg-2 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <div className="relative flex-shrink-0">
            <Activity size={14} className={activePanel === "activity" ? "text-accent-ink" : ""} />
            {hasNewToolCalls && activePanel !== "activity" && (
              <div className="absolute -top-0.5 -right-0.5 w-1.5 h-1.5 rounded-full bg-orange" />
            )}
          </div>
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
          onClick={() => onTogglePanel("agents")}
          title={collapsed ? "Agents" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            activePanel === "agents"
              ? "bg-bg-2 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <Users size={14} className={`flex-shrink-0 ${activePanel === "agents" ? "text-accent-ink" : ""}`} />
          {!collapsed && <span className="flex-1 text-left">Agents</span>}
        </button>
        <button
          onClick={onToggleDiag}
          title={collapsed ? "Diagnostics" : undefined}
          className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
            diagOpen
              ? "bg-bg-2 text-t-1"
              : "text-t-3 hover:bg-bg-2 hover:text-t-2"
          }`}
          style={collapsed ? { justifyContent: "center" } : undefined}
        >
          <Bug size={14} className={`flex-shrink-0 ${diagOpen ? "text-accent-ink" : ""}`} />
          {!collapsed && <span className="flex-1 text-left">Diagnostics</span>}
        </button>
      </nav>

      <div className="flex-1" />

      <SidebarFooter
        collapsed={collapsed}
        environment={
          dataverseConnected
            ? {
                name: environmentName || "Dataverse",
                url: environmentUrl ?? "",
                type: "Connected",
              }
            : { name: "Not connected", url: "", type: "Configure in Settings" }
        }
        tokenCount={tokenCount}
        onToggleCollapse={() => onCollapsedChange(!collapsed)}
        onTokenPillClick={onTokenPillClick}
        onSettingsClick={onSettingsClick}
      />
    </aside>
  );
}
