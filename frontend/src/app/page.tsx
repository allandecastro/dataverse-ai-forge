"use client";

import { useState, useEffect, useCallback } from "react";
import { Sparkles } from "lucide-react";
import { useCopilotReadable } from "@copilotkit/react-core";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar, type NavItemId } from "@/components/sidebar/Sidebar";
import { Dashboard } from "@/components/workspace/Dashboard";
import { ErdDiagram } from "@/components/workspace/ErdDiagram";
import { TableDetail } from "@/components/workspace/TableDetail";
import { ActivityLog } from "@/components/workspace/ActivityLog";
import { useDiagnostics } from "@/hooks/useDiagnostics";
import { useForgeApprovals } from "@/hooks/useForgeApprovals";
import { useTokenTracker } from "@/hooks/useTokenTracker";
import { useAgent } from "@/hooks/useAgent";

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
  | { kind: "table"; logicalName: string };

export default function Home() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const [chatOpen, setChatOpen] = useState(true);
  const [diagOpen, setDiagOpen] = useState(false);
  const [activeNav, setActiveNav] = useState<NavItemId>("overview");
  const [workspaceView, setWorkspaceView] = useState<WorkspaceView>({ kind: "overview" });
  const { entries } = useDiagnostics();
  useForgeApprovals();
  const { state: tokenState } = useTokenTracker();
  const { currentAgent, phase, tables, relationships, solutionName, solutionPrefix, solutionVersion } = useAgent();

  // Send workspace context to agents via CopilotKit readable state
  useCopilotReadable({
    description: "Current workspace context including active agent, phase, and created tables",
    value: {
      currentAgent,
      phase,
      tableCount: tables.length,
      tables: tables.map((t) => t.logicalName),
    },
  });

  useEffect(() => {
    localStorage.setItem("sidebar-collapsed", String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  // Sync sidebar nav to workspace view
  const handleNavChange = useCallback((id: NavItemId) => {
    setActiveNav(id);
    if (id === "overview") setWorkspaceView({ kind: "overview" });
    else if (id === "activity") setWorkspaceView({ kind: "activity" });
    else if (id === "erd") setWorkspaceView({ kind: "erd" });
  }, []);

  // Handle table selection from sidebar
  const handleSelectTable = useCallback((logicalName: string) => {
    setWorkspaceView({ kind: "table", logicalName });
  }, []);

  // Render workspace content based on active view
  function renderWorkspace() {
    switch (workspaceView.kind) {
      case "overview":
        return (
          <Dashboard
            solutionName={solutionName}
            solutionPrefix={solutionPrefix}
            solutionVersion={solutionVersion}
            tables={tables}
            relationships={relationships}
            entries={entries}
          />
        );
      case "erd":
        return <ErdDiagram tables={tables} relationships={relationships} />;
      case "activity":
        return <ActivityLog entries={entries} />;
      case "table": {
        const table = tables.find((t) => t.logicalName === workspaceView.logicalName);
        if (!table) {
          return (
            <div className="h-full flex items-center justify-center">
              <div className="text-t-3 text-sm">Table not found</div>
            </div>
          );
        }
        return <TableDetail table={table} relationships={relationships} />;
      }
    }
  }

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar
        collapsed={sidebarCollapsed}
        onCollapsedChange={setSidebarCollapsed}
        tokenCount={tokenState.totalTokens}
        cost={tokenState.totalCost}
        liveTables={tables}
        solutionName={solutionName}
        solutionPrefix={solutionPrefix}
        solutionVersion={solutionVersion}
        activeNav={activeNav}
        onNavChange={handleNavChange}
        onSelectTable={handleSelectTable}
        activityCount={entries.length}
      />

      {/* Workspace */}
      <main className="flex-1 bg-bg-0 overflow-hidden">
        {renderWorkspace()}
      </main>

      {/* Chat Panel */}
      {chatOpen && (
        <ChatPanel
          onClose={() => setChatOpen(false)}
          onToggleDiag={() => setDiagOpen(!diagOpen)}
          diagOpen={diagOpen}
          currentAgent={currentAgent}
        />
      )}

      {/* Diagnostics Panel */}
      {diagOpen && <DiagPanel entries={entries} connected={chatOpen} />}

      {/* Floating reopen button when chat is closed */}
      {!chatOpen && (
        <button
          onClick={() => setChatOpen(true)}
          className="fixed bottom-6 right-6 w-12 h-12 rounded-full bg-blue text-white flex items-center justify-center shadow-lg hover:opacity-90 transition-opacity"
        >
          <Sparkles size={20} />
        </button>
      )}
    </div>
  );
}
