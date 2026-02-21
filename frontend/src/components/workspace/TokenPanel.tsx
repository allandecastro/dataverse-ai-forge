"use client";

import { useMemo, useState, useEffect } from "react";
import { X, Zap, Clock, BarChart3 } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { IconBtn } from "@/components/primitives/IconBtn";
import { agents, defaultAgent } from "@/lib/agents";
import type { TokenTrackerState } from "@/lib/types";

interface TokenPanelProps {
  state: TokenTrackerState;
  sessionStart: number;
  onClose: () => void;
}

interface AgentRow {
  agentName: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  totalTokens: number;
  cost: number;
  callCount: number;
}

function formatTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1000) return `${(n / 1000).toFixed(1)}k`;
  return String(n);
}

function formatCost(n: number): string {
  if (n < 0.001 && n > 0) return "<$0.001";
  return `$${n.toFixed(3)}`;
}

function formatDuration(ms: number): string {
  if (ms < 1000) return `${ms}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  const mins = Math.floor(ms / 60_000);
  const secs = Math.round((ms % 60_000) / 1000);
  return `${mins}m ${secs}s`;
}

export function TokenPanel({
  state,
  sessionStart,
  onClose,
}: TokenPanelProps) {
  // Compute elapsed time with a ticking interval (avoids Date.now() in render)
  const [elapsed, setElapsed] = useState(0);
  useEffect(() => {
    const tick = () => setElapsed(Date.now() - sessionStart);
    tick();
    const id = setInterval(tick, 1000);
    return () => clearInterval(id);
  }, [sessionStart]);

  const agentRows = useMemo(() => {
    const map = new Map<string, AgentRow>();

    for (const entry of state.entries) {
      const existing = map.get(entry.agentName);
      if (existing) {
        existing.inputTokens += entry.inputTokens;
        existing.outputTokens += entry.outputTokens;
        existing.totalTokens += entry.totalTokens;
        existing.cost += entry.cost;
        existing.callCount += 1;
      } else {
        map.set(entry.agentName, {
          agentName: entry.agentName,
          model: entry.model,
          inputTokens: entry.inputTokens,
          outputTokens: entry.outputTokens,
          totalTokens: entry.totalTokens,
          cost: entry.cost,
          callCount: 1,
        });
      }
    }

    // Sort by cost descending
    return [...map.values()].sort((a, b) => b.cost - a.cost);
  }, [state.entries]);

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="bg-bg-1 border border-border rounded-xl shadow-2xl w-full max-w-2xl mx-4 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-border">
          <div className="flex items-center gap-2">
            <Zap size={16} className="text-orange" />
            <h2 className="text-t-1 text-base font-semibold">Session Cost</h2>
          </div>
          <div className="flex items-center gap-3">
            <span
              className="text-t-1 text-lg font-semibold"
              style={{ fontFamily: "var(--font-geist-mono), monospace" }}
            >
              ${state.totalCost.toFixed(2)}
            </span>
            <IconBtn icon={X} onClick={onClose} title="Close" />
          </div>
        </div>

        {/* Table */}
        <div className="px-5 py-4">
          {agentRows.length === 0 ? (
            <div className="text-t-3 text-sm text-center py-8">
              No token usage recorded yet
            </div>
          ) : (
            <div className="rounded-lg border border-border overflow-hidden">
              <table className="w-full text-xs">
                <thead>
                  <tr className="bg-bg-2 text-t-3">
                    <th className="text-left px-3 py-2.5 font-medium">
                      Agent
                    </th>
                    <th className="text-left px-3 py-2.5 font-medium w-20">
                      Model
                    </th>
                    <th className="text-right px-3 py-2.5 font-medium w-20">
                      Input
                    </th>
                    <th className="text-right px-3 py-2.5 font-medium w-20">
                      Output
                    </th>
                    <th className="text-right px-3 py-2.5 font-medium w-20">
                      Cost
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {agentRows.map((row, i) => {
                    const agent = agents[row.agentName] ?? defaultAgent;
                    const isGpt4o = row.model === "gpt-4o";

                    return (
                      <tr
                        key={row.agentName}
                        className={`${i % 2 === 0 ? "bg-bg-1" : "bg-bg-0"}`}
                      >
                        <td className="px-3 py-2">
                          <div className="flex items-center gap-2">
                            <AgentDot agentName={row.agentName} size={18} />
                            <span className="text-t-2 truncate">
                              {agent.displayName}
                            </span>
                            <span className="text-t-4" style={{ fontSize: 10 }}>
                              x{row.callCount}
                            </span>
                          </div>
                        </td>
                        <td className="px-3 py-2">
                          <span
                            className={isGpt4o ? "text-blue" : "text-green"}
                            style={{
                              fontFamily: "var(--font-geist-mono), monospace",
                              fontSize: 11,
                            }}
                          >
                            {isGpt4o ? "4o" : "4o-mini"}
                          </span>
                        </td>
                        <td
                          className="px-3 py-2 text-right text-t-3"
                          style={{
                            fontFamily: "var(--font-geist-mono), monospace",
                          }}
                        >
                          {formatTokens(row.inputTokens)}
                        </td>
                        <td
                          className="px-3 py-2 text-right text-t-3"
                          style={{
                            fontFamily: "var(--font-geist-mono), monospace",
                          }}
                        >
                          {formatTokens(row.outputTokens)}
                        </td>
                        <td
                          className="px-3 py-2 text-right text-t-2 font-medium"
                          style={{
                            fontFamily: "var(--font-geist-mono), monospace",
                          }}
                        >
                          {formatCost(row.cost)}
                        </td>
                      </tr>
                    );
                  })}

                  {/* Total row */}
                  <tr className="bg-bg-2 font-medium">
                    <td className="px-3 py-2 text-t-2">TOTAL</td>
                    <td className="px-3 py-2 text-t-4 text-xs">mixed</td>
                    <td
                      className="px-3 py-2 text-right text-t-2"
                      style={{
                        fontFamily: "var(--font-geist-mono), monospace",
                      }}
                    >
                      {formatTokens(
                        agentRows.reduce((s, r) => s + r.inputTokens, 0)
                      )}
                    </td>
                    <td
                      className="px-3 py-2 text-right text-t-2"
                      style={{
                        fontFamily: "var(--font-geist-mono), monospace",
                      }}
                    >
                      {formatTokens(
                        agentRows.reduce((s, r) => s + r.outputTokens, 0)
                      )}
                    </td>
                    <td
                      className="px-3 py-2 text-right text-t-1"
                      style={{
                        fontFamily: "var(--font-geist-mono), monospace",
                      }}
                    >
                      ${state.totalCost.toFixed(3)}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* Footer stats */}
        <div className="flex items-center gap-6 px-5 py-3 border-t border-border text-t-3 text-xs">
          <div className="flex items-center gap-1.5">
            <Clock size={12} />
            <span>Session: {formatDuration(elapsed)}</span>
          </div>
          <div className="flex items-center gap-1.5">
            <BarChart3 size={12} />
            <span>API calls: {state.entries.length}</span>
          </div>
          <div className="flex items-center gap-1.5">
            <Zap size={12} />
            <span>
              Avg:{" "}
              {state.entries.length > 0
                ? formatTokens(
                    Math.round(state.totalTokens / state.entries.length)
                  )
                : "0"}{" "}
              tokens/call
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
