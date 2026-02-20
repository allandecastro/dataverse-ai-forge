"use client";

import { useRef, useEffect } from "react";
import { Bug } from "lucide-react";
import { DiagEntry } from "./DiagEntry";
import type { DiagnosticEntry } from "@/lib/types";

interface DiagPanelProps {
  entries: DiagnosticEntry[];
  connected: boolean;
}

export function DiagPanel({ entries, connected }: DiagPanelProps) {
  const scrollRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom on new entries
  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [entries]);

  return (
    <section
      className="flex-shrink-0 border-l border-border bg-bg-1 flex flex-col"
      style={{ width: 380 }}
    >
      {/* Header */}
      <div className="flex h-12 items-center justify-between border-b border-border px-4">
        <div className="flex items-center gap-2">
          <Bug size={14} className="text-t-3" />
          <span className="text-t-2 text-sm font-medium">Diagnostics</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div
            className={`w-2 h-2 rounded-full ${
              connected ? "bg-green" : "bg-red"
            }`}
          />
          <span className="text-t-3 text-xs">
            {connected ? "Connected" : "Disconnected"}
          </span>
        </div>
      </div>

      {/* Entries */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto">
        {entries.length === 0 ? (
          <div className="flex flex-col items-center justify-center h-full text-t-3 text-xs gap-2">
            <Bug size={24} className="text-t-4" />
            <span>No tool calls yet</span>
          </div>
        ) : (
          entries.map((entry) => <DiagEntry key={entry.id} entry={entry} />)
        )}
      </div>
    </section>
  );
}
