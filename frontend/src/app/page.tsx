"use client";

import { useState, useEffect } from "react";
import { Sparkles } from "lucide-react";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { Sidebar } from "@/components/sidebar/Sidebar";
import { useDiagnostics } from "@/hooks/useDiagnostics";
import { useForgeApprovals } from "@/hooks/useForgeApprovals";

function getInitialSidebarCollapsed(): boolean {
  if (typeof window !== "undefined") {
    return localStorage.getItem("sidebar-collapsed") === "true";
  }
  return false;
}

export default function Home() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(getInitialSidebarCollapsed);
  const [chatOpen, setChatOpen] = useState(true);
  const [diagOpen, setDiagOpen] = useState(false);
  const { entries } = useDiagnostics();
  useForgeApprovals();

  useEffect(() => {
    localStorage.setItem("sidebar-collapsed", String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  return (
    <div className="flex h-screen overflow-hidden">
      <Sidebar collapsed={sidebarCollapsed} onCollapsedChange={setSidebarCollapsed} />

      {/* Workspace placeholder */}
      <main className="flex-1 bg-bg-0 flex items-center justify-center">
        <div className="text-t-3 text-sm">Workspace area</div>
      </main>

      {/* Chat Panel */}
      {chatOpen && (
        <ChatPanel
          onClose={() => setChatOpen(false)}
          onToggleDiag={() => setDiagOpen(!diagOpen)}
          diagOpen={diagOpen}
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
