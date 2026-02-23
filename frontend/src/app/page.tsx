"use client";

import { useState, useEffect, useCallback, useMemo, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { Sparkles } from "lucide-react";
import { useCopilotReadable } from "@copilotkit/react-core";
import { Providers } from "./providers";
import { DemoWorkspaceContent } from "./DemoWorkspaceContent";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar, type NavItemId } from "@/components/sidebar/Sidebar";
import { Dashboard } from "@/components/workspace/Dashboard";
import { ErdDiagram } from "@/components/workspace/ErdDiagram";
import { TableDetail } from "@/components/workspace/TableDetail";
import { ActivityLog } from "@/components/workspace/ActivityLog";
import { AgentTimeline } from "@/components/workspace/AgentTimeline";
import { TokenPanel } from "@/components/workspace/TokenPanel";
import { ProjectSelector } from "@/components/project/ProjectSelector";
import { SettingsModal } from "@/components/settings/SettingsModal";
import { useDiagnostics } from "@/hooks/useDiagnostics";
import { useForgeApprovals } from "@/hooks/useForgeApprovals";
import { useTokenTracker } from "@/hooks/useTokenTracker";
import { useAgent } from "@/hooks/useAgent";
import { useProjects } from "@/hooks/useProjects";
import { useAuditLog } from "@/hooks/useAuditLog";
import { mergeEntries } from "@/lib/mergeEntries";

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

/** Main workspace UI — rendered inside CopilotKit provider */
function WorkspaceContent({
  projectName,
  onSwitchProject,
  sessionId,
}: {
  projectName: string;
  onSwitchProject: () => void;
  sessionId: string;
}) {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const [chatOpen, setChatOpen] = useState(true);
  const [diagOpen, setDiagOpen] = useState(false);
  const [activeNav, setActiveNav] = useState<NavItemId>("overview");
  const [workspaceView, setWorkspaceView] = useState<WorkspaceView>({ kind: "overview" });
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [sessionStart] = useState(() => Date.now());
  const { entries } = useDiagnostics();
  const { auditEntries } = useAuditLog(sessionId);
  const mergedEntries = useMemo(() => mergeEntries(entries, auditEntries), [entries, auditEntries]);
  useForgeApprovals();
  const { state: tokenState } = useTokenTracker();
  const { currentAgent, phase, tables, relationships, solutionName, solutionPrefix, solutionVersion } = useAgent();

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
      case "timeline":
        return (
          <AgentTimeline
            entries={entries}
            totalCost={tokenState.totalCost}
            onJumpToDiag={() => {
              setDiagOpen(true);
            }}
          />
        );
      case "activity":
        return <ActivityLog entries={mergedEntries} />;
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
        onTokenPillClick={() => setTokenPanelOpen(true)}
        projectName={projectName}
        onSwitchProject={onSwitchProject}
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
        <ChatPanel
          onClose={() => setChatOpen(false)}
          onToggleDiag={() => setDiagOpen(!diagOpen)}
          diagOpen={diagOpen}
          currentAgent={currentAgent}
        />
      )}

      {diagOpen && <DiagPanel entries={entries} connected={chatOpen} />}

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
          state={tokenState}
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

function HomeInner() {
  const searchParams = useSearchParams();
  const isDemo = searchParams.get("demo") === "true";

  if (isDemo) {
    return <DemoWorkspaceContent />;
  }

  return <NormalHome />;
}

function NormalHome() {
  const {
    sessions,
    loading,
    selectedSessionId,
    selectSession,
    createNewSession,
    deleteSession,
  } = useProjects();

  // Track explicit "switch project" action separately
  const [selectorRequested, setSelectorRequested] = useState(false);

  // Show selector when no session is selected (derived) or explicitly requested
  const showSelector = selectorRequested || (!selectedSessionId && !loading);

  const handleNewProject = useCallback(() => {
    createNewSession();
    setSelectorRequested(false);
  }, [createNewSession]);

  const handleSelectSession = useCallback(
    (id: string) => {
      selectSession(id);
      setSelectorRequested(false);
    },
    [selectSession],
  );

  const handleSwitchProject = useCallback(() => {
    setSelectorRequested(true);
  }, []);

  // Find current session name for display
  const currentSession = sessions.find((s) => s.sessionId === selectedSessionId);
  const projectName = currentSession?.projectName || "New Project";

  if (!selectedSessionId && showSelector) {
    return (
      <ProjectSelector
        sessions={sessions}
        loading={loading}
        onSelect={handleSelectSession}
        onNewProject={handleNewProject}
        onDelete={deleteSession}
      />
    );
  }

  // Still loading session from localStorage
  if (!selectedSessionId) {
    return null;
  }

  return (
    <Providers threadId={selectedSessionId}>
      <WorkspaceContent
        projectName={projectName}
        onSwitchProject={handleSwitchProject}
        sessionId={selectedSessionId}
      />
      {showSelector && (
        <ProjectSelector
          sessions={sessions}
          loading={loading}
          onSelect={handleSelectSession}
          onNewProject={handleNewProject}
          onDelete={deleteSession}
        />
      )}
    </Providers>
  );
}

export default function Home() {
  return (
    <Suspense>
      <HomeInner />
    </Suspense>
  );
}
