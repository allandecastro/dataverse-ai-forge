"use client";

import { Terminal, CheckCircle2, AlertCircle } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { StreamingDots } from "@/components/primitives/StreamingDots";
import { Pill } from "@/components/primitives/Pill";
import { agents, defaultAgent } from "@/lib/agents";
import { formatTime, formatDuration } from "@/lib/formatting";
import type { DiagnosticEntry } from "@/lib/types";

interface ActivityLogProps {
  entries: DiagnosticEntry[];
}

function isHandoff(toolName: string): boolean {
  return toolName.startsWith("HandoffTo");
}

export function ActivityLog({ entries }: ActivityLogProps) {
  // Show newest first
  const sorted = [...entries].reverse();

  if (sorted.length === 0) {
    return (
      <div className="h-full flex items-center justify-center">
        <div className="text-t-3 text-sm text-center">
          <div className="text-t-4 mb-2">No activity yet</div>
          <div className="text-t-4 text-xs">
            Tool calls will appear here as agents execute operations
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full overflow-y-auto p-6">
      <h1 className="text-t-1 text-xl font-semibold mb-4">Activity Log</h1>

      <div className="rounded-lg border border-border overflow-hidden">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-bg-2 text-t-3">
              <th className="text-left px-3 py-2.5 font-medium w-20">
                Time
              </th>
              <th className="text-left px-3 py-2.5 font-medium w-44">
                Agent
              </th>
              <th className="text-left px-3 py-2.5 font-medium">Tool</th>
              <th className="text-right px-3 py-2.5 font-medium w-20">
                Duration
              </th>
              <th className="text-center px-3 py-2.5 font-medium w-16">
                Status
              </th>
            </tr>
          </thead>
          <tbody>
            {sorted.map((entry, i) => {
              const agent = agents[entry.agent] ?? defaultAgent;
              const handoff = isHandoff(entry.toolName);

              return (
                <tr
                  key={entry.id}
                  className={`${i % 2 === 0 ? "bg-bg-1" : "bg-bg-0"} hover:bg-bg-2 transition-colors`}
                >
                  <td
                    className="px-3 py-2 text-t-3"
                    style={{
                      fontFamily: "var(--font-geist-mono), monospace",
                    }}
                  >
                    {formatTime(entry.timestamp)}
                  </td>
                  <td className="px-3 py-2">
                    <div className="flex items-center gap-2">
                      <AgentDot agentName={entry.agent} size={18} />
                      <span className="text-t-2 truncate">
                        {agent.displayName}
                      </span>
                    </div>
                  </td>
                  <td className="px-3 py-2">
                    <div className="flex items-center gap-1.5">
                      <Terminal size={11} className="text-t-4 flex-shrink-0" />
                      <span
                        className={`truncate ${handoff ? "text-blue" : "text-t-2"}`}
                        style={{
                          fontFamily: "var(--font-geist-mono), monospace",
                        }}
                      >
                        {entry.toolName}
                      </span>
                      {handoff && (
                        <Pill label="handoff" color="blue" />
                      )}
                    </div>
                  </td>
                  <td
                    className="px-3 py-2 text-right text-t-3"
                    style={{
                      fontFamily: "var(--font-geist-mono), monospace",
                    }}
                  >
                    {formatDuration(entry.duration)}
                  </td>
                  <td className="px-3 py-2 text-center">
                    {entry.status === "complete" ? (
                      <CheckCircle2
                        size={14}
                        className="text-green mx-auto"
                      />
                    ) : entry.status === "error" ? (
                      <AlertCircle
                        size={14}
                        className="text-red mx-auto"
                      />
                    ) : (
                      <StreamingDots color="bg-orange" size={1} />
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div className="text-t-4 text-xs mt-3 text-right">
        {entries.length} event{entries.length !== 1 ? "s" : ""} recorded
      </div>
    </div>
  );
}
