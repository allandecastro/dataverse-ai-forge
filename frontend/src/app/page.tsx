"use client";

import { useState, useEffect, useCallback, useMemo, Suspense } from "react";
import { useSearchParams } from "next/navigation";
import { Providers } from "./providers";
import { DemoWorkspaceContent } from "./DemoWorkspaceContent";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar } from "@/components/sidebar/Sidebar";
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
  const [diagOpen, setDiagOpen] = useState(false);
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [sessionStart] = useState(() => Date.now());
  const { entries } = useDiagnostics();
  const { auditEntries } = useAuditLog(sessionId);
  const merged = useMemo(() => mergeEntries(entries, auditEntries), [entries, auditEntries]);
  const { approvalElement } = useForgeApprovals();
  const { state: tokenState, addUsage } = useTokenTracker();
  const { currentAgent, solutionName, solutionPrefix, solutionVersion } = useAgent();
  const { lastUsage } = useForgeChatContext();

  useEffect(() => {
    if (lastUsage && lastUsage.totalTokens > 0) {
      addUsage(currentAgent, lastUsage.inputTokens, lastUsage.outputTokens);
    }
  }, [lastUsage, currentAgent, addUsage]);

  useEffect(() => {
    localStorage.setItem("sidebar-collapsed", String(sidebarCollapsed));
  }, [sidebarCollapsed]);

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
        onTokenPillClick={() => setTokenPanelOpen(true)}
        projectName={projectName}
        onSwitchProject={onSwitchProject}
        onSettingsClick={() => setSettingsOpen(true)}
      />

      <ChatPanel
        onToggleDiag={() => setDiagOpen(!diagOpen)}
        diagOpen={diagOpen}
        currentAgent={currentAgent}
        entries={entries}
        mergedEntries={merged}
        totalCost={tokenState.totalCost}
      />

      {diagOpen && (
        <DiagPanel
          entries={entries}
          connected={true}
          onClose={() => setDiagOpen(false)}
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
