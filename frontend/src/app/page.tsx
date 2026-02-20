"use client";

import { useState } from "react";
import { Sparkles } from "lucide-react";
import { ChatPanel } from "@/components/chat/ChatPanel";
import { DiagPanel } from "@/components/diagnostics/DiagPanel";
import { useDiagnostics } from "@/hooks/useDiagnostics";

export default function Home() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [chatOpen, setChatOpen] = useState(true);
  const [diagOpen, setDiagOpen] = useState(false);
  const { entries } = useDiagnostics();

  return (
    <div className="flex h-screen overflow-hidden">
      {/* Sidebar placeholder */}
      <aside
        className="flex-shrink-0 border-r border-border bg-bg-1 transition-all duration-200"
        style={{ width: sidebarCollapsed ? 52 : 252 }}
      >
        <div className="flex h-12 items-center justify-center border-b border-border">
          <button
            onClick={() => setSidebarCollapsed(!sidebarCollapsed)}
            className="text-t-3 hover:text-t-1 text-xs"
          >
            {sidebarCollapsed ? ">>" : "<<"}
          </button>
        </div>
        <div className="p-3 text-t-3 text-xs">
          {!sidebarCollapsed && "Solution tree coming soon..."}
        </div>
      </aside>

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
