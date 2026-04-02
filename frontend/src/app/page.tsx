"use client";

import { useState, useEffect, useCallback, useMemo, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { Sparkles } from "lucide-react";
import { Providers } from "./providers";
import { DemoWorkspaceContent } from "./DemoWorkspaceContent";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar, type NavItemId } from "@/components/sidebar/Sidebar";
import { ActivityLog } from "@/components/workspace/ActivityLog";
import { AgentTimeline } from "@/components/workspace/AgentTimeline";
import { TokenPanel } from "@/components/workspace/TokenPanel";
import { ProjectSelector } from "@/components/project/ProjectSelector";
import { SettingsModal } from "@/components/settings/SettingsModal";
import { useDiagnostics } from "@/hooks/useDiagnostics";
import { useForgeApprovals } from "@/hooks/useForgeApprovals";
import { useTokenTracker } from "@/hooks/useTokenTracker";
import { useAgent } from "@/hooks/useAgent";
import { useForgeChatContext } from "@/hooks/useForgeChat";
import { useProjects } from "@/hooks/useProjects";
import { useAuditLog } from "@/hooks/useAuditLog";
import { mergeEntries } from "@/lib/mergeEntries";

function getInitialSidebarCollapsed(): boolean {
  if (typeof window !== "undefined") {
    return localStorage.getItem("sidebar-collapsed") === "true";
  }
  return false;
}

const DEFAULT_CHAT_WIDTH = 520;

function getInitialChatWidth(): number {
  if (typeof window !== "undefined") {
    const stored = localStorage.getItem("chat-width");
    if (stored) {
      const n = parseInt(stored, 10);
      if (n >= 320 && n <= 900) return n;
    }
  }
  return DEFAULT_CHAT_WIDTH;
}

type WorkspaceView =
  | { kind: "activity" }
  | { kind: "timeline" };

/** Main workspace UI — rendered inside ForgeChatProvider */
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
  const [activeNav, setActiveNav] = useState<NavItemId>("timeline");
  const [workspaceView, setWorkspaceView] = useState<WorkspaceView>({ kind: "timeline" });
  const [chatWidth, setChatWidth] = useState(getInitialChatWidth);
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [sessionStart] = useState(() => Date.now());
  const { entries } = useDiagnostics();
  const { auditEntries } = useAuditLog(sessionId);
  const mergedEntries = useMemo(() => mergeEntries(entries, auditEntries), [entries, auditEntries]);
  const { approvalElement } = useForgeApprovals();
  const { state: tokenState, addUsage } = useTokenTracker();
  const { currentAgent, solutionName, solutionPrefix, solutionVersion } = useAgent();
  const { lastUsage } = useForgeChatContext();

  // Wire token tracking from SSE RUN_FINISHED metadata
  useEffect(() => {
    if (lastUsage && lastUsage.totalTokens > 0) {
      addUsage(currentAgent, lastUsage.inputTokens, lastUsage.outputTokens);
    }
  }, [lastUsage, currentAgent, addUsage]);

  useEffect(() => {
    localStorage.setItem("chat-width", String(chatWidth));
  }, [chatWidth]);

  useEffect(() => {
    localStorage.setItem("sidebar-collapsed", String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  const handleNavChange = useCallback((id: NavItemId) => {
    setActiveNav(id);
    if (id === "activity") setWorkspaceView({ kind: "activity" });
    else if (id === "timeline") setWorkspaceView({ kind: "timeline" });
  }, []);

  function renderWorkspace() {
    switch (workspaceView.kind) {
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
    }
  }

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar
        collapsed={sidebarCollapsed}
        onCollapsedChange={setSidebarCollapsed}
        tokenCount={tokenState.totalTokens}
        cost={tokenState.totalCost}
        solutionName={solutionName}
        solutionPrefix={solutionPrefix}
        solutionVersion={solutionVersion}
        activeNav={activeNav}
        onNavChange={handleNavChange}
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
          width={chatWidth}
          onWidthChange={setChatWidth}
        />
      )}

      {diagOpen && <DiagPanel entries={entries} connected={chatOpen} />}

      {/* Approval overlay */}
      {approvalElement && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="max-w-lg w-full mx-4">{approvalElement}</div>
        </div>
      )}

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

  const [selectorRequested, setSelectorRequested] = useState(false);
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
