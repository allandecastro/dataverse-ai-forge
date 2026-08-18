"use client";

import { Fragment, useState } from "react";
import { Terminal, CheckCircle2, AlertCircle, ChevronRight } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { StreamingDots } from "@/components/primitives/StreamingDots";
import { Pill } from "@/components/primitives/Pill";
import { ActivityMetrics } from "./ActivityMetrics";
import { agents, defaultAgent } from "@/lib/agents";
import { formatTime, formatDuration } from "@/lib/formatting";
import type { DiagnosticEntry } from "@/lib/types";

interface ActivityLogProps {
  entries: DiagnosticEntry[];
}

function isHandoff(toolName: string): boolean {
  return toolName.startsWith("HandoffTo");
}

/** Pretty-print a tool payload/result. Strings pass through; objects are JSON
 *  with 2-space indent so args and Dataverse responses stay readable. */
function stringifyDetail(value: unknown): string {
  if (value == null) return "";
  if (typeof value === "string") return value;
  try {
    return JSON.stringify(value, null, 2);
  } catch {
    return String(value);
  }
}

export function ActivityLog({ entries }: ActivityLogProps) {
  // Show newest first
  const sorted = [...entries].reverse();
  const [expandedId, setExpandedId] = useState<string | null>(null);

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

      <ActivityMetrics entries={entries} />

      {/* overflow-x-auto lets the table scroll instead of pushing the Tool
          column past the panel edge when the workspace is narrow. */}
      <div className="rounded-lg border border-border overflow-x-auto">
        <table className="w-full min-w-[540px] text-xs">
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
              const rowBg = i % 2 === 0 ? "bg-bg-1" : "bg-bg-0";
              const payload = stringifyDetail(entry.payload);
              const result = stringifyDetail(entry.result);
              const hasDetail = payload.length > 0 || result.length > 0;
              const isExpanded = expandedId === entry.id;

              return (
                <Fragment key={entry.id}>
                  <tr
                    className={`${rowBg} ${hasDetail ? "cursor-pointer hover:bg-bg-2" : ""} transition-colors`}
                    onClick={() =>
                      hasDetail && setExpandedId(isExpanded ? null : entry.id)
                    }
                  >
                    <td className="px-3 py-2 text-t-3 font-mono align-top">
                      {formatTime(entry.timestamp)}
                    </td>
                    <td className="px-3 py-2 align-top">
                      <div className="flex items-center gap-2">
                        <AgentDot agentName={entry.agent} size={18} />
                        <span className="text-t-2 truncate">
                          {agent.displayName}
                        </span>
                      </div>
                    </td>
                    <td className="px-3 py-2 align-top">
                      <div className="flex items-center gap-1.5 min-w-0">
                        {hasDetail ? (
                          <ChevronRight
                            size={12}
                            className={`text-t-4 flex-shrink-0 transition-transform ${isExpanded ? "rotate-90" : ""}`}
                          />
                        ) : (
                          <Terminal size={11} className="text-t-4 flex-shrink-0" />
                        )}
                        <span
                          className={`truncate font-mono ${handoff ? "text-blue" : "text-t-2"}`}
                        >
                          {entry.toolName}
                        </span>
                        {handoff && <Pill label="handoff" color="blue" />}
                      </div>
                    </td>
                    <td className="px-3 py-2 text-right text-t-3 font-mono align-top whitespace-nowrap">
                      {formatDuration(entry.duration)}
                    </td>
                    <td className="px-3 py-2 text-center align-top">
                      {entry.status === "complete" ? (
                        <CheckCircle2 size={14} className="text-green mx-auto" />
                      ) : entry.status === "error" ? (
                        <AlertCircle size={14} className="text-red mx-auto" />
                      ) : (
                        <StreamingDots color="bg-blue" size={1} />
                      )}
                    </td>
                  </tr>

                  {isExpanded && (
                    <tr className={rowBg}>
                      <td colSpan={5} className="px-3 pb-3 pt-0">
                        <div className="flex flex-col gap-2">
                          {payload && (
                            <DetailBlock label="Payload" body={payload} />
                          )}
                          {result && (
                            <DetailBlock
                              label="Result"
                              body={result}
                              error={entry.status === "error"}
                            />
                          )}
                        </div>
                      </td>
                    </tr>
                  )}
                </Fragment>
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

function DetailBlock({
  label,
  body,
  error,
}: {
  label: string;
  body: string;
  error?: boolean;
}) {
  return (
    <div>
      <div className="text-t-4 text-[10px] uppercase tracking-wide mb-1">
        {label}
      </div>
      <pre
        className={`bg-code-bg text-code-text font-mono text-[11px] leading-relaxed px-2.5 py-2 overflow-x-auto max-h-64 overflow-y-auto whitespace-pre-wrap break-words ${error ? "text-red" : ""}`}
      >
        {body}
      </pre>
    </div>
  );
}
