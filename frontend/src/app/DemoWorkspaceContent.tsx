"use client";

import { useState, useEffect, useCallback } from "react";
import { X } from "lucide-react";
import { ThemeProvider } from "@/components/layout/ThemeProvider";
import { ToastProvider } from "@/contexts/ToastContext";
import { STORAGE_SIDEBAR_COLLAPSED } from "@/lib/constants";
import { DemoChatPanel } from "@/components/chat/DemoChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar, type PanelId } from "@/components/sidebar/Sidebar";
import { SidebarProvider } from "@/components/sidebar/SidebarContext";
import { AgentTimeline } from "@/components/workspace/AgentTimeline";
import { ActivityLog } from "@/components/workspace/ActivityLog";
import { AgentsPanel } from "@/components/workspace/AgentsPanel";
import { TokenPanel } from "@/components/workspace/TokenPanel";
import { SettingsModal } from "@/components/settings/SettingsModal";
import {
  demoDiagnosticEntries,
  demoTokenState,
  demoSolutionMeta,
} from "@/lib/demo-scenario";

function getInitialSidebarCollapsed(): boolean {
  if (typeof window !== "undefined") {
    return localStorage.getItem(STORAGE_SIDEBAR_COLLAPSED) === "true";
  }
  return false;
}

function DemoWorkspaceInner() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const [diagOpen, setDiagOpen] = useState(false);
  const [activePanel, setActivePanel] = useState<PanelId | null>(null);
  const [middlePanelWidth, setMiddlePanelWidth] = useState(420);
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [sessionStart] = useState(() => Date.now() - 540_000);

  useEffect(() => {
    localStorage.setItem(STORAGE_SIDEBAR_COLLAPSED, String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  const handleTogglePanel = useCallback((id: PanelId) => {
    setActivePanel((prev) => (prev === id ? null : id));
  }, []);

  return (
    <SidebarProvider value={{
      collapsed: sidebarCollapsed,
      onCollapsedChange: setSidebarCollapsed,
      tokenCount: demoTokenState.totalTokens,
      solutionName: demoSolutionMeta.name,
      solutionPrefix: demoSolutionMeta.prefix,
      solutionVersion: demoSolutionMeta.version,
      onTokenPillClick: () => setTokenPanelOpen(true),
      projectName: "Contoso Feedback (Demo)",
      onSwitchProject: () => { window.location.href = "/"; },
      onSettingsClick: () => setSettingsOpen(true),
      activePanel,
      onTogglePanel: handleTogglePanel,
      activityCount: demoDiagnosticEntries.length,
      diagOpen,
      onToggleDiag: () => setDiagOpen(!diagOpen),
    }}>
    <div className="flex h-dvh overflow-hidden">
      <Sidebar />

      {/* Middle panel — Timeline or Activity (resizable) */}
      {activePanel && (
        <div
          className="flex-shrink-0 bg-bg-0 flex flex-col overflow-hidden relative"
          style={{ width: middlePanelWidth, animation: "phase-fade-in 0.15s ease-out" }}
        >
          <div className="flex h-12 items-center justify-between border-b border-border px-3">
            <span className="text-t-1 text-sm font-semibold">
              {activePanel === "timeline"
                ? "Timeline"
                : activePanel === "activity"
                  ? "Activity Log"
                  : "Agents"}
            </span>
            <button
              onClick={() => setActivePanel(null)}
              className="flex items-center justify-center rounded p-1 text-t-3 hover:text-t-1 hover:bg-bg-2 transition-colors"
            >
              <X size={14} />
            </button>
          </div>
          <div className="flex-1 overflow-hidden">
            {activePanel === "timeline" && (
              <AgentTimeline
                entries={demoDiagnosticEntries}
                totalTokens={demoTokenState.totalTokens}
                onJumpToDiag={() => setDiagOpen(true)}
              />
            )}
            {activePanel === "activity" && (
              <ActivityLog entries={demoDiagnosticEntries} />
            )}
            {activePanel === "agents" && <AgentsPanel />}
          </div>
          {/* Resize handle on right edge */}
          <div
            onMouseDown={(e) => {
              e.preventDefault();
              const startX = e.clientX;
              const startW = middlePanelWidth;
              const move = (ev: MouseEvent) => {
                const newW = Math.max(200, startW + (ev.clientX - startX));
                setMiddlePanelWidth(newW);
              };
              const up = () => {
                document.removeEventListener("mousemove", move);
                document.removeEventListener("mouseup", up);
                document.body.style.cursor = "";
                document.body.style.userSelect = "";
              };
              document.addEventListener("mousemove", move);
              document.addEventListener("mouseup", up);
              document.body.style.cursor = "col-resize";
              document.body.style.userSelect = "none";
            }}
            className="absolute right-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-accent/40 active:bg-accent/60 transition-colors z-10"
          />
        </div>
      )}

      <DemoChatPanel
        onToggleDiag={() => setDiagOpen(!diagOpen)}
        diagOpen={diagOpen}
      />

      {diagOpen && (
        <DiagPanel
          entries={demoDiagnosticEntries}
          connected={true}
          onClose={() => setDiagOpen(false)}
        />
      )}

      {tokenPanelOpen && (
        <TokenPanel
          state={demoTokenState}
          sessionStart={sessionStart}
          onClose={() => setTokenPanelOpen(false)}
        />
      )}

      {settingsOpen && (
        <SettingsModal onClose={() => setSettingsOpen(false)} />
      )}
    </div>
    </SidebarProvider>
  );
}

export function DemoWorkspaceContent() {
  return (
    <ThemeProvider>
      <ToastProvider>
        <DemoWorkspaceInner />
      </ToastProvider>
    </ThemeProvider>
  );
}
