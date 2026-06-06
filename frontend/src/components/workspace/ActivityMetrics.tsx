"use client";

import { useMemo } from "react";
import { ArrowRightLeft, Terminal, AlertCircle, Users, Clock } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { agents, defaultAgent } from "@/lib/agents";
import { formatDuration } from "@/lib/formatting";
import type { DiagnosticEntry } from "@/lib/types";

interface ActivityMetricsProps {
  entries: DiagnosticEntry[];
}

interface PerAgent {
  agent: string;
  toolCalls: number;
  handoffs: number;
}

/**
 * Top-of-page summary tiles for the activity log. Five high-signal counters
 * plus a per-agent breakdown so the user can see at a glance who's been doing
 * what without scrolling the table.
 *
 * "Handoff" detection mirrors the table's row check — any tool starting with
 * `HandoffTo`. Errors come from the `status === 'error'` flag the parser sets
 * when result text starts with `error:` or the SSE event was an explicit error.
 */
export function ActivityMetrics({ entries }: ActivityMetricsProps) {
  const stats = useMemo(() => {
    let handoffs = 0;
    let errors = 0;
    let totalDurationMs = 0;
    const perAgentMap = new Map<string, PerAgent>();

    for (const e of entries) {
      const isHandoff = e.toolName.startsWith("HandoffTo");
      if (isHandoff) handoffs++;
      if (e.status === "error") errors++;
      if (e.duration != null) totalDurationMs += e.duration;

      const slot = perAgentMap.get(e.agent) ?? {
        agent: e.agent,
        toolCalls: 0,
        handoffs: 0,
      };
      slot.toolCalls++;
      if (isHandoff) slot.handoffs++;
      perAgentMap.set(e.agent, slot);
    }

    const perAgent = [...perAgentMap.values()].sort(
      (a, b) => b.toolCalls - a.toolCalls,
    );

    return {
      total: entries.length,
      handoffs,
      errors,
      agentsInvolved: perAgentMap.size,
      totalDurationMs,
      perAgent,
    };
  }, [entries]);

  if (entries.length === 0) return null;

  return (
    <div className="mb-4 flex flex-col gap-3">
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-2">
        <Tile
          icon={<Terminal size={14} />}
          label="Tool calls"
          value={stats.total.toLocaleString()}
          color="text-t-1"
        />
        <Tile
          icon={<ArrowRightLeft size={14} />}
          label="Handoffs"
          value={stats.handoffs.toLocaleString()}
          color="text-blue"
        />
        <Tile
          icon={<AlertCircle size={14} />}
          label="Errors"
          value={stats.errors.toLocaleString()}
          color={stats.errors > 0 ? "text-red" : "text-t-1"}
        />
        <Tile
          icon={<Users size={14} />}
          label="Agents involved"
          value={stats.agentsInvolved.toLocaleString()}
          color="text-t-1"
        />
        <Tile
          icon={<Clock size={14} />}
          label="Tool time"
          value={formatDuration(stats.totalDurationMs)}
          color="text-t-1"
        />
      </div>

      {stats.perAgent.length > 0 && (
        <div className="rounded-lg border border-border bg-bg-1 px-3 py-2">
          <div className="text-t-3 text-xs mb-1.5 font-medium">Per agent</div>
          <div className="flex flex-wrap gap-x-4 gap-y-1.5">
            {stats.perAgent.map((row) => {
              const agent = agents[row.agent] ?? defaultAgent;
              return (
                <div key={row.agent} className="flex items-center gap-2 text-xs">
                  <AgentDot agentName={row.agent} size={14} />
                  <span className="text-t-2">{agent.displayName}</span>
                  <span
                    className="text-t-3 font-mono"
                    title={`${row.toolCalls} tool call${row.toolCalls !== 1 ? "s" : ""}, ${row.handoffs} handoff${row.handoffs !== 1 ? "s" : ""}`}
                  >
                    {row.toolCalls}
                    {row.handoffs > 0 && (
                      <span className="text-blue ml-1">
                        ({row.handoffs} HO)
                      </span>
                    )}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}

interface TileProps {
  icon: React.ReactNode;
  label: string;
  value: string;
  color: string;
}

function Tile({ icon, label, value, color }: TileProps) {
  return (
    <div className="rounded-lg border border-border bg-bg-1 px-3 py-2">
      <div className="flex items-center gap-1.5 text-t-3 text-xs mb-1">
        {icon}
        <span>{label}</span>
      </div>
      <div
        className={`text-base font-semibold font-mono ${color}`}
      >
        {value}
      </div>
    </div>
  );
}
