"use client";

import { useState, useEffect } from "react";
import { ThemeProvider } from "@/components/layout/ThemeProvider";
import { ToastProvider } from "@/contexts/ToastContext";
import { DemoChatPanel } from "@/components/chat/DemoChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar } from "@/components/sidebar/Sidebar";
import { TokenPanel } from "@/components/workspace/TokenPanel";
import { SettingsModal } from "@/components/settings/SettingsModal";
import {
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

function DemoWorkspaceInner() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const [diagOpen, setDiagOpen] = useState(false);
  const [tokenPanelOpen, setTokenPanelOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [sessionStart] = useState(() => Date.now() - 540_000);

  useEffect(() => {
    localStorage.setItem("sidebar-collapsed", String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar
        collapsed={sidebarCollapsed}
        onCollapsedChange={setSidebarCollapsed}
        tokenCount={demoTokenState.totalTokens}
        cost={demoTokenState.totalCost}
        solutionName={demoSolutionMeta.name}
        solutionPrefix={demoSolutionMeta.prefix}
        solutionVersion={demoSolutionMeta.version}
        onTokenPillClick={() => setTokenPanelOpen(true)}
        projectName="Contoso Feedback (Demo)"
        onSwitchProject={() => {
          window.location.href = "/";
        }}
        onSettingsClick={() => setSettingsOpen(true)}
      />

      <DemoChatPanel
        onToggleDiag={() => setDiagOpen(!diagOpen)}
        diagOpen={diagOpen}
        entries={demoDiagnosticEntries}
        totalCost={demoTokenState.totalCost}
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
