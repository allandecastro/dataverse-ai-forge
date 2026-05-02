"use client";

import { useRef, useEffect, useState, useMemo } from "react";
import { Bug, Search, X } from "lucide-react";
import { DiagEntry } from "./DiagEntry";
import { agents } from "@/lib/agents";
import type { DiagnosticEntry } from "@/lib/types";

type StatusFilter = "all" | "complete" | "error";

interface DiagPanelProps {
  entries: DiagnosticEntry[];
  connected: boolean;
  onClose: () => void;
  initialAgentFilter?: string;
  initialEntryId?: string;
}

export function DiagPanel({ entries, connected, onClose, initialAgentFilter, initialEntryId }: DiagPanelProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const [agentFilter, setAgentFilter] = useState(initialAgentFilter ?? "all");
  const [toolSearch, setToolSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  // Scroll to specific entry if initialEntryId is provided
  useEffect(() => {
    if (initialEntryId && scrollRef.current) {
      const el = scrollRef.current.querySelector(`[data-entry-id="${initialEntryId}"]`);
      if (el) {
        el.scrollIntoView({ behavior: "smooth", block: "center" });
      }
    }
  }, [initialEntryId]);

  // Keep scroll at the TOP — entries are sorted newest-first below, so the most
  // recent diagnostic is always immediately visible without needing to scroll.
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = 0;
    }
  }, [entries]);

  const filteredEntries = useMemo(() => {
    // Show newest first — long sessions accumulate hundreds of tool calls and
    // scrolling to the bottom every time is friction. The data is naturally
    // chronological in `entries`; we reverse it here so the latest sits at top.
    const filtered = entries.filter((entry) => {
      if (agentFilter !== "all" && entry.agent !== agentFilter) return false;
      if (toolSearch && !entry.toolName.toLowerCase().includes(toolSearch.toLowerCase())) return false;
      if (statusFilter === "complete" && entry.status !== "complete") return false;
      if (statusFilter === "error" && entry.status !== "error") return false;
      return true;
    });
    return [...filtered].reverse();
  }, [entries, agentFilter, toolSearch, statusFilter]);

  const statusButtons: { label: string; value: StatusFilter }[] = [
    { label: "All", value: "all" },
    { label: "Complete", value: "complete" },
    { label: "Error", value: "error" },
  ];

  return (
    <>
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-black/30 z-30"
        onClick={onClose}
      />

      {/* Slide-over panel */}
      <section
        className="fixed top-0 right-0 bottom-0 z-40 bg-bg-1 border-l border-border flex flex-col shadow-xl"
        style={{ width: 420, animation: "slide-in-right 0.2s ease-out" }}
      >
        {/* Header */}
        <div className="flex h-12 items-center justify-between border-b border-border px-4">
          <div className="flex items-center gap-2">
            <Bug size={14} className="text-t-3" />
            <span className="text-t-2 text-sm font-medium">Diagnostics</span>
          </div>
          <div className="flex items-center gap-3">
            <div
              className="flex items-center gap-1.5"
              title="SSE stream status — whether the chat connection to the backend is live"
            >
              <div
                className={`w-2 h-2 rounded-full ${
                  connected ? "bg-green" : "bg-red"
                }`}
              />
              <span className="text-t-3 text-xs">
                {connected ? "Stream live" : "Disconnected"}
              </span>
            </div>
            <button
              onClick={onClose}
              className="flex items-center justify-center rounded p-1 text-t-3 hover:text-t-1 hover:bg-bg-2 transition-colors"
            >
              <X size={14} />
            </button>
          </div>
        </div>

        {/* Filter bar */}
        <div className="border-b border-border px-3 py-2 flex flex-col gap-2">
          <div className="flex gap-2">
            <select
              value={agentFilter}
              onChange={(e) => setAgentFilter(e.target.value)}
              className="flex-1 bg-bg-2 border border-border rounded px-2 py-1 text-xs text-t-2 outline-none"
            >
              <option value="all">All Agents</option>
              {Object.values(agents).map((a) => (
                <option key={a.name} value={a.name}>
                  {a.displayName}
                </option>
              ))}
            </select>
            <div className="flex-1 relative">
              <Search size={12} className="absolute left-2 top-1/2 -translate-y-1/2 text-t-4" />
              <input
                type="text"
                value={toolSearch}
                onChange={(e) => setToolSearch(e.target.value)}
                placeholder="Search tools..."
                className="w-full bg-bg-2 border border-border rounded pl-6 pr-2 py-1 text-xs text-t-2 outline-none placeholder:text-t-4"
              />
            </div>
          </div>
          <div className="flex items-center gap-1">
            {statusButtons.map((btn) => (
              <button
                key={btn.value}
                onClick={() => setStatusFilter(btn.value)}
                className={`px-2.5 py-0.5 rounded-full text-xs font-medium transition-colors ${
                  statusFilter === btn.value
                    ? "bg-blue text-white"
                    : "bg-bg-3 text-t-3 hover:text-t-2"
                }`}
              >
                {btn.label}
              </button>
            ))}
            <span className="ml-auto text-t-4 text-xs">
              {filteredEntries.length} of {entries.length} entries
            </span>
          </div>
        </div>

        {/* Entries */}
        <div ref={scrollRef} className="flex-1 overflow-y-auto">
          {filteredEntries.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full text-t-3 text-xs gap-2">
              <Bug size={24} className="text-t-4" />
              <span>{entries.length === 0 ? "No tool calls yet" : "No matching entries"}</span>
            </div>
          ) : (
            filteredEntries.map((entry) => <DiagEntry key={entry.id} entry={entry} />)
          )}
        </div>
      </section>
    </>
  );
}
