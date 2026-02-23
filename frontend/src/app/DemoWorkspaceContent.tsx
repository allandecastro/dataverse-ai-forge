"use client";

import { useState, useEffect, useCallback } from "react";
import { Sparkles } from "lucide-react";
import { ThemeProvider } from "@/components/layout/ThemeProvider";
import { ToastProvider } from "@/contexts/ToastContext";
import { DemoChatPanel } from "@/components/chat/DemoChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar, type NavItemId } from "@/components/sidebar/Sidebar";
import { Dashboard } from "@/components/workspace/Dashboard";
import { ErdDiagram } from "@/components/workspace/ErdDiagram";
import { TableDetail } from "@/components/workspace/TableDetail";
import { ActivityLog } from "@/components/workspace/ActivityLog";
import { AgentTimeline } from "@/components/workspace/AgentTimeline";
import { TokenPanel } from "@/components/workspace/TokenPanel";
import { SettingsModal } from "@/components/settings/SettingsModal";
import {
  demoTables,
  demoRelationships,
  demoDiagnosticEntries,
  demoTokenState,
  demoSolutionMeta,
} from "@/lib/demo-scenario";

function getInitialSidebarCollapsed(): boolean {
  if (typeof window !== "undefined") {
    return localStorage.getItem("sidebar-collapsed") === "true";
  }
  return false;
}

type WorkspaceView =
  | { kind: "overview" }
  | { kind: "activity" }
  | { kind: "erd" }
  | { kind: "timeline" }
  | { kind: "table"; logicalName: string };

function DemoWorkspaceInner() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const [chatOpen, setChatOpen] = useState(true);
  const [diagOpen, setDiagOpen] = useState(false);
  const [activeNav, setActiveNav] = useState<NavItemId>("overview");
  const [workspaceView, setWorkspaceView] = useState<WorkspaceView>({ kind: "overview" });
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [sessionStart] = useState(() => Date.now() - 540_000); // 9 min ago

  useEffect(() => {
    localStorage.setItem("sidebar-collapsed", String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  const handleNavChange = useCallback((id: NavItemId) => {
    setActiveNav(id);
    if (id === "overview") setWorkspaceView({ kind: "overview" });
    else if (id === "activity") setWorkspaceView({ kind: "activity" });
    else if (id === "erd") setWorkspaceView({ kind: "erd" });
    else if (id === "timeline") setWorkspaceView({ kind: "timeline" });
  }, []);

  const handleSelectTable = useCallback((logicalName: string) => {
    setWorkspaceView({ kind: "table", logicalName });
  }, []);

  function renderWorkspace() {
    switch (workspaceView.kind) {
      case "overview":
        return (
          <Dashboard
            solutionName={demoSolutionMeta.name}
            solutionPrefix={demoSolutionMeta.prefix}
            solutionVersion={demoSolutionMeta.version}
            tables={demoTables}
            relationships={demoRelationships}
            entries={demoDiagnosticEntries}
          />
        );
      case "erd":
        return <ErdDiagram tables={demoTables} relationships={demoRelationships} />;
      case "timeline":
        return (
          <AgentTimeline
            entries={demoDiagnosticEntries}
            totalCost={demoTokenState.totalCost}
            onJumpToDiag={() => {
              setDiagOpen(true);
            }}
          />
        );
      case "activity":
        return <ActivityLog entries={demoDiagnosticEntries} />;
      case "table": {
        const table = demoTables.find((t) => t.logicalName === workspaceView.logicalName);
        if (!table) {
          return (
            <div className="h-full flex items-center justify-center">
              <div className="text-t-3 text-sm">Table not found</div>
            </div>
          );
        }
        return <TableDetail table={table} relationships={demoRelationships} />;
      }
    }
  }

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar
        collapsed={sidebarCollapsed}
        onCollapsedChange={setSidebarCollapsed}
        tokenCount={demoTokenState.totalTokens}
        cost={demoTokenState.totalCost}
        liveTables={demoTables}
        solutionName={demoSolutionMeta.name}
        solutionPrefix={demoSolutionMeta.prefix}
        solutionVersion={demoSolutionMeta.version}
        activeNav={activeNav}
        onNavChange={handleNavChange}
        onSelectTable={handleSelectTable}
        activityCount={demoDiagnosticEntries.length}
        onTokenPillClick={() => setTokenPanelOpen(true)}
        projectName="Contoso Feedback (Demo)"
        onSwitchProject={() => {
          // In demo mode, navigate to normal mode
          window.location.href = "/";
        }}
        onSettingsClick={() => setSettingsOpen(true)}
      />

      <main
        key={workspaceView.kind}
        className="flex-1 bg-bg-0 overflow-hidden"
        style={{ animation: "phase-fade-in 0.25s ease-out" }}
      >
        {renderWorkspace()}
      </main>

      {chatOpen && (
        <DemoChatPanel
          onClose={() => setChatOpen(false)}
          onToggleDiag={() => setDiagOpen(!diagOpen)}
          diagOpen={diagOpen}
        />
      )}

      {diagOpen && <DiagPanel entries={demoDiagnosticEntries} connected={chatOpen} />}

      {!chatOpen && (
        <button
          onClick={() => setChatOpen(true)}
          className="fixed bottom-6 right-6 w-12 h-12 rounded-full bg-blue text-white flex items-center justify-center shadow-lg hover:opacity-90 transition-opacity"
        >
          <Sparkles size={20} />
        </button>
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
