"use client";

import { useState, useEffect, useCallback, useMemo, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { X } from "lucide-react";
import { STORAGE_SIDEBAR_COLLAPSED } from "@/lib/constants";
import { Providers } from "./providers";
import { DemoWorkspaceContent } from "./DemoWorkspaceContent";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar, type PanelId } from "@/components/sidebar/Sidebar";
import { SidebarProvider } from "@/components/sidebar/SidebarContext";
import { OfflineBanner } from "@/components/layout/OfflineBanner";
import { AgentTimeline } from "@/components/workspace/AgentTimeline";
import { ActivityLog } from "@/components/workspace/ActivityLog";
import { TokenPanel } from "@/components/workspace/TokenPanel";
import { ProjectSelector } from "@/components/project/ProjectSelector";
import { SettingsModal } from "@/components/settings/SettingsModal";
import { useDiagnostics } from "@/hooks/useDiagnostics";
import { useForgeApprovals } from "@/hooks/useForgeApprovals";
import { useTokenTracker } from "@/hooks/useTokenTracker";
import { useAgent } from "@/hooks/useAgent";
import { useForgeChatContext } from "@/hooks/useForgeChat";
import { useProjects } from "@/hooks/useProjects";
import { useSettings } from "@/hooks/useSettings";
import { useAuditLog } from "@/hooks/useAuditLog";
import { mergeEntries } from "@/lib/mergeEntries";

function getInitialSidebarCollapsed(): boolean {
  if (typeof window !== "undefined") {
    return localStorage.getItem(STORAGE_SIDEBAR_COLLAPSED) === "true";
  }
  return false;
}

/** Main workspace UI — rendered inside ForgeChatProvider */
function WorkspaceContent({
  projectName,
  onSwitchProject,
  onRenameProject,
  sessionId,
}: {
  projectName: string;
  onSwitchProject: () => void;
  onRenameProject: (newName: string) => Promise<boolean>;
  sessionId: string;
}) {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const [diagOpen, setDiagOpen] = useState(false);
  const [diagAgentFilter, setDiagAgentFilter] = useState<string | undefined>();
  const [diagEntryId, setDiagEntryId] = useState<string | undefined>();
  const [activePanel, setActivePanel] = useState<PanelId | null>(null);
  const [middlePanelWidth, setMiddlePanelWidth] = useState(420);
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [sessionStart] = useState(() => Date.now());
  const [seenToolCallCount, setSeenToolCallCount] = useState(0);
  const { entries } = useDiagnostics();
  const { auditEntries } = useAuditLog(sessionId);
  const merged = useMemo(() => mergeEntries(entries, auditEntries), [entries, auditEntries]);
  const { approvalElement } = useForgeApprovals();
  const { state: tokenState, addUsage } = useTokenTracker();
  const { currentAgent, environmentName, environmentUrl, solutionName, solutionPrefix, solutionVersion } = useAgent();
  const { lastUsage } = useForgeChatContext();
  const { health, fetchHealth } = useSettings();

  // Refresh health on mount so the sidebar reflects the saved Dataverse connection
  // immediately, before any agent run pushes a STATE_DELTA. Then poll every 30s
  // so a backend crash mid-session surfaces as the OfflineBanner instead of
  // a "Failed to fetch" toast on the user's next action.
  //
  // The interval skips work when the tab is hidden (Chrome already throttles
  // background timers, but we also don't want a stale "offline" banner from
  // before the user switched away — visibilitychange forces a fresh fetch the
  // moment they return).
  useEffect(() => {
    void fetchHealth();
    const interval = setInterval(() => {
      if (document.visibilityState === "hidden") return;
      void fetchHealth();
    }, 30_000);
    const onVisible = () => {
      if (document.visibilityState === "visible") void fetchHealth();
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      clearInterval(interval);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [fetchHealth]);

  const backendOffline = health?.status === "unavailable";

  // Prefer agent-provided env (most fresh) but fall back to the health probe so
  // the sidebar lights up green right after the user saves working credentials.
  const effectiveEnvName = environmentName || health?.dataverse?.environment || "";
  const effectiveEnvUrl = environmentUrl || health?.dataverse?.environmentUrl || "";
  const dataverseConnected = !!effectiveEnvName || health?.dataverse?.status === "connected";

  useEffect(() => {
    if (lastUsage && lastUsage.totalTokens > 0) {
      addUsage(currentAgent, lastUsage.inputTokens, lastUsage.outputTokens);
    }
  }, [lastUsage, currentAgent, addUsage]);

  useEffect(() => {
    localStorage.setItem(STORAGE_SIDEBAR_COLLAPSED, String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  const hasNewToolCalls = entries.length > seenToolCallCount;

  const handleTogglePanel = useCallback((id: PanelId) => {
    setActivePanel((prev) => {
      if (prev !== id) setSeenToolCallCount(entries.length);
      return prev === id ? null : id;
    });
  }, [entries.length]);

  return (
    <SidebarProvider value={{
      collapsed: sidebarCollapsed,
      onCollapsedChange: setSidebarCollapsed,
      tokenCount: tokenState.totalTokens,
      environmentName: effectiveEnvName,
      environmentUrl: effectiveEnvUrl,
      dataverseConnected,
      solutionName,
      solutionPrefix,
      solutionVersion,
      onTokenPillClick: () => setTokenPanelOpen(true),
      projectName,
      onSwitchProject,
      onRenameProject,
      onSettingsClick: () => setSettingsOpen(true),
      activePanel,
      onTogglePanel: handleTogglePanel,
      activityCount: entries.length,
      diagOpen,
      onToggleDiag: () => { setDiagOpen(!diagOpen); setDiagAgentFilter(undefined); setDiagEntryId(undefined); },
      hasNewToolCalls,
    }}>
    <div className="flex flex-col h-screen overflow-hidden">
      {backendOffline && <OfflineBanner onRetry={() => void fetchHealth()} />}
      <div className="flex flex-1 min-h-0 overflow-hidden">
      <Sidebar />

      {/* Middle panel — Timeline or Activity (hidden by default, resizable) */}
      {activePanel && (
        <div
          className="flex-shrink-0 bg-bg-0 flex flex-col overflow-hidden relative"
          style={{ width: middlePanelWidth, animation: "phase-fade-in 0.15s ease-out" }}
        >
          <div className="flex h-12 items-center justify-between border-b border-border px-3">
            <span className="text-t-2 text-sm font-medium">
              {activePanel === "timeline" ? "Timeline" : "Activity Log"}
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
                entries={entries}
                totalTokens={tokenState.totalTokens}
                onJumpToDiag={(entryId) => {
                  setDiagEntryId(entryId);
                  setDiagAgentFilter(undefined);
                  setDiagOpen(true);
                }}
                onFilterByAgent={(agentName) => {
                  setDiagAgentFilter(agentName);
                  setDiagEntryId(undefined);
                  setDiagOpen(true);
                }}
              />
            )}
            {activePanel === "activity" && (
              <ActivityLog entries={merged} />
            )}
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
            className="absolute right-0 top-0 bottom-0 w-1 cursor-col-resize hover:bg-blue/40 active:bg-blue/60 transition-colors z-10"
          />
        </div>
      )}

      <ChatPanel
        onToggleDiag={() => setDiagOpen(!diagOpen)}
        diagOpen={diagOpen}
        currentAgent={currentAgent}
      />

      {diagOpen && (
        <DiagPanel
          entries={entries}
          connected={true}
          onClose={() => { setDiagOpen(false); setDiagAgentFilter(undefined); setDiagEntryId(undefined); }}
          initialAgentFilter={diagAgentFilter}
          initialEntryId={diagEntryId}
        />
      )}

      {approvalElement && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="max-w-lg w-full mx-4">{approvalElement}</div>
        </div>
      )}

      {tokenPanelOpen && (
        <TokenPanel
          state={tokenState}
          sessionStart={sessionStart}
          onClose={() => setTokenPanelOpen(false)}
        />
      )}

      {settingsOpen && (
        <SettingsModal
          onClose={() => {
            setSettingsOpen(false);
            // Re-probe health so the sidebar reflects any saved credential change
            void fetchHealth();
          }}
        />
      )}
      </div>
    </div>
    </SidebarProvider>
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
    renameSession,
    deleteSession,
  } = useProjects();

  const [selectorRequested, setSelectorRequested] = useState(false);
  const showSelector = selectorRequested || (!selectedSessionId && !loading);

  const handleNewProject = useCallback(
    async (name: string) => {
      const id = await createNewSession(name);
      if (id) setSelectorRequested(false);
      return id;
    },
    [createNewSession],
  );

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

  const handleRenameProject = useCallback(
    async (newName: string) => {
      if (!selectedSessionId) return false;
      return await renameSession(selectedSessionId, newName);
    },
    [selectedSessionId, renameSession],
  );

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
        onRenameProject={handleRenameProject}
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
