"use client";

import { useMemo, useState, useCallback } from "react";
import { Clock, Zap } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { Pill } from "@/components/primitives/Pill";
import { agents, defaultAgent } from "@/lib/agents";
import { formatElapsed, formatTokens, formatDuration } from "@/lib/formatting";
import type { DiagnosticEntry } from "@/lib/types";

interface AgentTimelineProps {
  entries: DiagnosticEntry[];
  totalTokens: number;
  onJumpToDiag?: (entryId: string) => void;
  onFilterByAgent?: (agentName: string) => void;
}

interface TimelineSegment {
  entry: DiagnosticEntry;
  startOffset: number;
  duration: number;
}

interface AgentRow {
  agentName: string;
  segments: TimelineSegment[];
}

export function AgentTimeline({
  entries,
  totalTokens,
  onJumpToDiag,
  onFilterByAgent,
}: AgentTimelineProps) {
  const [hoveredSegment, setHoveredSegment] = useState<string | null>(null);
  // The scrubber dims events after the playhead so the user can walk the run
  // forward manually. Starts at 100% (all events shown). Auto-playback was
  // removed: watching it advance on its own added nothing over dragging it.
  const [playheadPosition, setPlayheadPosition] = useState(100);

  const { rows, totalElapsed, timeScale, allSegments } = useMemo(() => {
    if (entries.length === 0) {
      return { rows: [], totalElapsed: 0, timeScale: [] as number[], allSegments: [] as TimelineSegment[] };
    }

    const sessionStart = Math.min(...entries.map((e) => e.timestamp));
    const sessionEnd = Math.max(
      ...entries.map((e) => e.timestamp + (e.duration ?? 500))
    );
    const elapsed = sessionEnd - sessionStart;

    // Group entries by agent
    const agentMap = new Map<string, TimelineSegment[]>();
    const all: TimelineSegment[] = [];
    for (const entry of entries) {
      const startOffset = entry.timestamp - sessionStart;
      const dur = entry.duration ?? 500;
      const segment: TimelineSegment = { entry, startOffset, duration: dur };
      all.push(segment);

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

    return { rows: agentRows, totalElapsed: elapsed, timeScale: scale, allSegments: all };
  }, [entries]);

  const handleScrub = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setPlayheadPosition(Number(e.target.value));
  }, []);

  // Count events before playhead
  const playheadMs = (playheadPosition / 100) * totalElapsed;
  const eventsBeforePlayhead = allSegments.filter(
    (s) => s.startOffset <= playheadMs
  ).length;

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

  // Scale the track width with event density so each segment stays readable.
  // Below ~16 events the 800px default is fine; above that we widen the track
  // and the outer card scrolls horizontally.
  const trackWidth = Math.max(800, allSegments.length * 50);

  return (
    <div className="h-full overflow-auto p-6">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-t-1 text-xl font-semibold">Agent Timeline</h1>
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-1.5 text-t-3 text-xs">
            <Clock size={13} />
            <span className="font-mono">
              {formatElapsed(totalElapsed)}
            </span>
          </div>
          <div className="flex items-center gap-1.5 text-t-3 text-xs">
            <Zap size={13} />
            <span className="font-mono">
              {formatTokens(totalTokens)} tokens
            </span>
          </div>
        </div>
      </div>

      {/* Timeline — horizontal scroll kicks in when trackWidth exceeds the viewport.
          The Agent column is sticky-left so labels stay visible while scrubbing. */}
      <div className="rounded-lg border border-border overflow-x-auto bg-bg-1">
        {/* Time scale header */}
        <div className="flex border-b border-border-lt">
          <div className="w-40 flex-shrink-0 sticky left-0 z-20 bg-bg-1 px-3 py-2 text-t-4 text-xs font-medium border-r border-border-lt">
            Agent
          </div>
          <div className="flex-1 relative px-2 py-2" style={{ minWidth: trackWidth }}>
            <div className="flex justify-between">
              {timeScale.map((tick, i) => (
                <span
                  key={i}
                  className="text-t-4 font-mono"
                  style={{ fontSize: 10 }}
                >
                  {formatElapsed(tick)}
                </span>
              ))}
            </div>
          </div>
        </div>

        {/* Scrubber bar */}
        <div className="flex border-b border-border-lt">
          <div className="w-40 flex-shrink-0 sticky left-0 z-20 bg-bg-1 px-3 py-1 flex items-center border-r border-border-lt">
            <span
              className="text-t-4 font-mono"
              style={{ fontSize: 10 }}
            >
              Events: {eventsBeforePlayhead}/{allSegments.length}
            </span>
          </div>
          <div className="flex-1 relative px-2 py-1 flex items-center" style={{ minWidth: trackWidth }}>
            <input
              type="range"
              min={0}
              max={100}
              step={0.1}
              value={playheadPosition}
              onChange={handleScrub}
              className="w-full h-1 appearance-none bg-bg-3 rounded cursor-pointer accent-red"
              style={{ accentColor: "var(--red)" }}
            />
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
              {/* Agent label — click to filter diagnostics by agent.
                  sticky-left + matching row bg keeps it visible during horizontal scroll. */}
              <button
                onClick={() => onFilterByAgent?.(row.agentName)}
                className={`w-40 flex-shrink-0 sticky left-0 z-10 px-3 py-3 flex items-center gap-2 hover:bg-bg-2 transition-colors text-left border-r border-border-lt ${
                  rowIdx % 2 === 0 ? "bg-bg-1" : "bg-bg-0"
                }`}
                title={`View all diagnostics for ${agent.displayName}`}
              >
                <AgentDot agentName={row.agentName} size={20} />
                <span className="text-t-2 text-xs truncate">
                  {agent.displayName}
                </span>
              </button>

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

                {/* Playhead vertical line */}
                {playheadPosition < 100 && (
                  <div
                    className="absolute top-0 bottom-0 w-px bg-red z-10 pointer-events-none"
                    style={{ left: `${playheadPosition}%` }}
                  />
                )}

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
                  const isFuture = playheadPosition < 100 && leftPct > playheadPosition;

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
                        opacity: isFuture ? 0.2 : isHovered ? 1 : 0.75,
                      }}
                      onMouseEnter={() => setHoveredSegment(seg.entry.id)}
                      onMouseLeave={() => setHoveredSegment(null)}
                      onClick={() => onJumpToDiag?.(seg.entry.id)}
                    >
                      {/* Tooltip */}
                      {isHovered && (
                        <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 z-50 pointer-events-none">
                          <div className="bg-bg-3 border border-border-hi px-2.5 py-1.5 whitespace-nowrap">
                            <div className="text-t-1 text-xs font-medium font-mono">
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
