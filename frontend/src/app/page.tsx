"use client";

import { useState } from "react";

export default function Home() {
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [chatOpen, setChatOpen] = useState(true);
  const [diagOpen, setDiagOpen] = useState(false);

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
          {!sidebarCollapsed && (
            <button
              onClick={() => setDiagOpen(!diagOpen)}
              className="hover:text-t-1"
            >
              {diagOpen ? "Hide" : "Show"} diagnostics
            </button>
          )}
        </div>
      </aside>

      {/* Workspace placeholder */}
      <main className="flex-1 bg-bg-0 flex items-center justify-center">
        <div className="text-t-3 text-sm">Workspace area</div>
      </main>

      {/* Chat Panel placeholder — replaced in Phase 0F */}
      {chatOpen && (
        <section
          className="flex-shrink-0 border-l border-border bg-bg-1 flex flex-col"
          style={{ width: 348 }}
        >
          <div className="flex h-12 items-center justify-between border-b border-border px-4">
            <span className="text-t-2 text-sm font-medium">Assistant</span>
            <button
              onClick={() => setChatOpen(false)}
              className="text-t-3 hover:text-t-1 text-xs"
            >
              X
            </button>
          </div>
          <div className="flex-1 flex items-center justify-center">
            <span className="text-t-3 text-xs">
              Chat panel coming in Phase 0F...
            </span>
          </div>
        </section>
      )}

      {/* Diagnostics Panel placeholder — replaced in Phase 1D */}
      {diagOpen && (
        <section
          className="flex-shrink-0 border-l border-border bg-bg-1 flex flex-col"
          style={{ width: 380 }}
        >
          <div className="flex h-12 items-center justify-between border-b border-border px-4">
            <span className="text-t-2 text-sm font-medium">Diagnostics</span>
          </div>
          <div className="flex-1 flex items-center justify-center">
            <span className="text-t-3 text-xs">
              Diagnostics panel coming in Phase 1D...
            </span>
          </div>
        </section>
      )}

      {/* Floating reopen button when chat is closed */}
      {!chatOpen && (
        <button
          onClick={() => setChatOpen(true)}
          className="fixed bottom-6 right-6 w-12 h-12 rounded-full bg-blue text-white flex items-center justify-center shadow-lg hover:opacity-90 transition-opacity"
        >
          <span className="text-lg">AI</span>
        </button>
      )}
    </div>
  );
}
