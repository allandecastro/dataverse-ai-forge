"use client";

import { useState, useCallback, useRef } from "react";
import { Timer, Activity, ChevronUp, ChevronDown } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import { AgentTimeline } from "@/components/workspace/AgentTimeline";
import { ActivityLog } from "@/components/workspace/ActivityLog";
import type { DiagnosticEntry } from "@/lib/types";

type TabId = "timeline" | "activity";

interface BottomPanelProps {
  entries: DiagnosticEntry[];
  mergedEntries: DiagnosticEntry[];
  totalCost: number;
  onToggleDiag: () => void;
}

const MIN_HEIGHT = 120;
const MAX_HEIGHT = 500;
const DEFAULT_HEIGHT = 240;

function getInitialHeight(): number {
  if (typeof window !== "undefined") {
    const stored = localStorage.getItem("bottom-panel-height");
    if (stored) {
      const n = parseInt(stored, 10);
      if (n >= MIN_HEIGHT && n <= MAX_HEIGHT) return n;
    }
  }
  return DEFAULT_HEIGHT;
}

export function BottomPanel({
  entries,
  mergedEntries,
  totalCost,
  onToggleDiag,
}: BottomPanelProps) {
  const [expanded, setExpanded] = useState(false);
  const [activeTab, setActiveTab] = useState<TabId>("timeline");
  const [height, setHeight] = useState(getInitialHeight);
  const draggingRef = useRef(false);

  const toggle = useCallback(() => {
    setExpanded((prev) => !prev);
  }, []);

  const handleMouseDown = useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      draggingRef.current = true;
      const startY = e.clientY;
      const startHeight = height;

      const handleMouseMove = (moveEvent: MouseEvent) => {
        if (!draggingRef.current) return;
        const delta = startY - moveEvent.clientY;
        const newHeight = Math.min(MAX_HEIGHT, Math.max(MIN_HEIGHT, startHeight + delta));
        setHeight(newHeight);
        localStorage.setItem("bottom-panel-height", String(newHeight));
      };

      const handleMouseUp = () => {
        draggingRef.current = false;
        document.removeEventListener("mousemove", handleMouseMove);
        document.removeEventListener("mouseup", handleMouseUp);
        document.body.style.cursor = "";
        document.body.style.userSelect = "";
      };

      document.addEventListener("mousemove", handleMouseMove);
      document.addEventListener("mouseup", handleMouseUp);
      document.body.style.cursor = "row-resize";
      document.body.style.userSelect = "none";
    },
    [height]
  );

  return (
    <div className="flex-shrink-0 border-t border-border">
      {/* Tab bar — always visible */}
      <div className="flex items-center h-8 px-2 gap-1 bg-bg-1">
        <button
          onClick={() => { setActiveTab("timeline"); if (!expanded) setExpanded(true); }}
          className={`flex items-center gap-1.5 px-2 py-1 rounded text-xs transition-colors ${
            activeTab === "timeline" && expanded
              ? "bg-bg-3 text-t-1"
              : "text-t-3 hover:text-t-2 hover:bg-bg-2"
          }`}
        >
          <Timer size={12} />
          Timeline
        </button>
        <button
          onClick={() => { setActiveTab("activity"); if (!expanded) setExpanded(true); }}
          className={`flex items-center gap-1.5 px-2 py-1 rounded text-xs transition-colors ${
            activeTab === "activity" && expanded
              ? "bg-bg-3 text-t-1"
              : "text-t-3 hover:text-t-2 hover:bg-bg-2"
          }`}
        >
          <Activity size={12} />
          Activity
          {entries.length > 0 && (
            <Pill label={String(entries.length)} color="neutral" />
          )}
        </button>

        <div className="flex-1" />

        <button
          onClick={toggle}
          className="flex items-center justify-center rounded p-0.5 text-t-3 hover:text-t-2 hover:bg-bg-2 transition-colors"
          title={expanded ? "Collapse panel" : "Expand panel"}
        >
          {expanded ? <ChevronDown size={14} /> : <ChevronUp size={14} />}
        </button>
      </div>

      {/* Content — collapsible */}
      {expanded && (
        <div className="relative bg-bg-0" style={{ height }}>
          {/* Resize handle */}
          <div
            onMouseDown={handleMouseDown}
            className="absolute top-0 left-0 right-0 h-1 cursor-row-resize hover:bg-blue/40 active:bg-blue/60 transition-colors z-10"
            style={{ marginTop: -2 }}
          />

          <div className="h-full overflow-hidden">
            {activeTab === "timeline" && (
              <AgentTimeline
                entries={entries}
                totalCost={totalCost}
                onJumpToDiag={onToggleDiag}
              />
            )}
            {activeTab === "activity" && (
              <ActivityLog entries={mergedEntries} />
            )}
          </div>
        </div>
      )}
    </div>
  );
}
