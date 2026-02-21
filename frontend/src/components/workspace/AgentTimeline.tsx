"use client";

import { useMemo, useState } from "react";
import { Clock, DollarSign } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { Pill } from "@/components/primitives/Pill";
import { agents, defaultAgent } from "@/lib/agents";
import type { DiagnosticEntry } from "@/lib/types";

interface AgentTimelineProps {
  entries: DiagnosticEntry[];
  totalCost: number;
  onJumpToDiag?: (entryId: string) => void;
}

interface TimelineSegment {
  entry: DiagnosticEntry;
  startOffset: number; // ms from session start
  duration: number; // ms
}

interface AgentRow {
  agentName: string;
  segments: TimelineSegment[];
}

function formatElapsed(ms: number): string {
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  const mins = Math.floor(ms / 60_000);
  const secs = ((ms % 60_000) / 1000).toFixed(0);
  return `${mins}m ${secs}s`;
}

function formatCost(n: number): string {
  return `$${n.toFixed(3)}`;
}

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

export function AgentTimeline({
  entries,
  totalCost,
  onJumpToDiag,
}: AgentTimelineProps) {
  const [hoveredSegment, setHoveredSegment] = useState<string | null>(null);

  const { rows, totalElapsed, timeScale } = useMemo(() => {
    if (entries.length === 0) {
      return { rows: [], totalElapsed: 0, timeScale: [] as number[] };
    }

    const sessionStart = Math.min(...entries.map((e) => e.timestamp));
    const sessionEnd = Math.max(
      ...entries.map((e) => e.timestamp + (e.duration ?? 500))
    );
    const elapsed = sessionEnd - sessionStart;

    // Group entries by agent
    const agentMap = new Map<string, TimelineSegment[]>();
    for (const entry of entries) {
      const startOffset = entry.timestamp - sessionStart;
      const dur = entry.duration ?? 500;
      const segment: TimelineSegment = { entry, startOffset, duration: dur };

      const existing = agentMap.get(entry.agent);
      if (existing) {
        existing.push(segment);
      } else {
        agentMap.set(entry.agent, [segment]);
      }
    }

    // Sort agents by first appearance
    const sorted = [...agentMap.entries()].sort((a, b) => {
      const aFirst = Math.min(...a[1].map((s) => s.startOffset));
      const bFirst = Math.min(...b[1].map((s) => s.startOffset));
      return aFirst - bFirst;
    });

    const agentRows: AgentRow[] = sorted.map(([agentName, segments]) => ({
      agentName,
      segments: segments.sort((a, b) => a.startOffset - b.startOffset),
    }));

    // Time scale ticks
    const tickCount = Math.min(Math.max(Math.ceil(elapsed / 2000), 3), 12);
    const tickInterval = elapsed / tickCount;
    const scale: number[] = [];
    for (let i = 0; i <= tickCount; i++) {
      scale.push(i * tickInterval);
    }

    return { rows: agentRows, totalElapsed: elapsed, timeScale: scale };
  }, [entries]);

  if (entries.length === 0) {
    return (
      <div className="h-full flex items-center justify-center">
        <div className="text-t-3 text-sm text-center">
          <div className="text-t-4 mb-2">No agent activity yet</div>
          <div className="text-t-4 text-xs">
            Timeline will populate as agents execute operations
          </div>
        </div>
      </div>
    );
  }

  const trackWidth = 800; // pixels for the timeline track

  return (
    <div className="h-full overflow-auto p-6">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-t-1 text-xl font-semibold">Agent Timeline</h1>
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-1.5 text-t-3 text-xs">
            <Clock size={13} />
            <span
              style={{
                fontFamily: "var(--font-geist-mono), monospace",
              }}
            >
              {formatElapsed(totalElapsed)}
            </span>
          </div>
          <div className="flex items-center gap-1.5 text-t-3 text-xs">
            <DollarSign size={13} />
            <span
              style={{
                fontFamily: "var(--font-geist-mono), monospace",
              }}
            >
              {formatCost(totalCost)}
            </span>
          </div>
        </div>
      </div>

      {/* Timeline */}
      <div className="rounded-lg border border-border overflow-hidden bg-bg-1">
        {/* Time scale header */}
        <div className="flex border-b border-border-lt">
          <div className="w-40 flex-shrink-0 px-3 py-2 text-t-4 text-xs font-medium">
            Agent
          </div>
          <div className="flex-1 relative px-2 py-2" style={{ minWidth: trackWidth }}>
            <div className="flex justify-between">
              {timeScale.map((tick, i) => (
                <span
                  key={i}
                  className="text-t-4"
                  style={{
                    fontSize: 10,
                    fontFamily: "var(--font-geist-mono), monospace",
                  }}
                >
                  {formatElapsed(tick)}
                </span>
              ))}
            </div>
          </div>
        </div>

        {/* Agent rows */}
        {rows.map((row, rowIdx) => {
          const agent = agents[row.agentName] ?? defaultAgent;

          return (
            <div
              key={row.agentName}
              className={`flex items-center border-b border-border-lt last:border-b-0 ${
                rowIdx % 2 === 0 ? "bg-bg-1" : "bg-bg-0"
              }`}
            >
              {/* Agent label */}
              <div className="w-40 flex-shrink-0 px-3 py-3 flex items-center gap-2">
                <AgentDot agentName={row.agentName} size={20} />
                <span className="text-t-2 text-xs truncate">
                  {agent.displayName}
                </span>
              </div>

              {/* Timeline track */}
              <div
                className="flex-1 relative px-2"
                style={{ minWidth: trackWidth, height: 36 }}
              >
                {/* Grid lines */}
                {timeScale.map((tick, i) => (
                  <div
                    key={i}
                    className="absolute top-0 bottom-0 border-l border-border-lt"
                    style={{
                      left: `${(tick / totalElapsed) * 100}%`,
                      opacity: 0.3,
                    }}
                  />
                ))}

                {/* Segments */}
                {row.segments.map((seg) => {
                  const leftPct = (seg.startOffset / totalElapsed) * 100;
                  const widthPct = Math.max(
                    (seg.duration / totalElapsed) * 100,
                    0.5
                  );
                  const isHovered = hoveredSegment === seg.entry.id;
                  const isHandoff = seg.entry.toolName.startsWith("HandoffTo");
                  const isError = seg.entry.status === "error";

                  return (
                    <div
                      key={seg.entry.id}
                      className="absolute top-1/2 -translate-y-1/2 rounded-sm cursor-pointer transition-opacity"
                      style={{
                        left: `${leftPct}%`,
                        width: `${widthPct}%`,
                        minWidth: 4,
                        height: 18,
                        backgroundColor: isError
                          ? "var(--red)"
                          : isHandoff
                            ? "var(--blue)"
                            : agent.color,
                        opacity: isHovered ? 1 : 0.75,
                      }}
                      onMouseEnter={() => setHoveredSegment(seg.entry.id)}
                      onMouseLeave={() => setHoveredSegment(null)}
                      onClick={() => onJumpToDiag?.(seg.entry.id)}
                    >
                      {/* Tooltip */}
                      {isHovered && (
                        <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 z-50 pointer-events-none">
                          <div className="bg-bg-3 border border-border rounded-md px-2.5 py-1.5 shadow-lg whitespace-nowrap">
                            <div
                              className="text-t-1 text-xs font-medium"
                              style={{
                                fontFamily:
                                  "var(--font-geist-mono), monospace",
                              }}
                            >
                              {seg.entry.toolName}
                            </div>
                            <div className="flex items-center gap-2 mt-0.5">
                              <span className="text-t-3 text-xs">
                                {formatDuration(seg.duration)}
                              </span>
                              <Pill
                                label={seg.entry.status}
                                color={
                                  seg.entry.status === "complete"
                                    ? "green"
                                    : seg.entry.status === "error"
                                      ? "red"
                                      : "neutral"
                                }
                              />
                            </div>
                          </div>
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>

      {/* Legend */}
      <div className="flex items-center gap-4 mt-3 text-t-4 text-xs">
        <div className="flex items-center gap-1.5">
          <div
            className="w-3 h-2 rounded-sm"
            style={{ backgroundColor: "var(--blue)", opacity: 0.75 }}
          />
          <span>Active</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div
            className="w-3 h-2 rounded-sm"
            style={{ backgroundColor: "var(--red)", opacity: 0.75 }}
          />
          <span>Error</span>
        </div>
        <span className="ml-auto">
          {entries.length} event{entries.length !== 1 ? "s" : ""} recorded
        </span>
      </div>
    </div>
  );
}
