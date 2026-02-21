"use client";

import {
  Database,
  Columns3,
  Link2,
  CheckCircle2,
  Clock,
  Terminal,
} from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import { AgentDot } from "@/components/primitives/AgentDot";
import type { SolutionTable, SolutionRelationship } from "@/hooks/useAgent";
import type { DiagnosticEntry } from "@/lib/types";

interface DashboardProps {
  solutionName: string;
  solutionPrefix: string;
  solutionVersion: string;
  tables: SolutionTable[];
  relationships: SolutionRelationship[];
  entries: DiagnosticEntry[];
}

interface StatCardProps {
  icon: typeof Database;
  label: string;
  value: number;
  color: string;
}

function StatCard({ icon: Icon, label, value, color }: StatCardProps) {
  return (
    <div className="rounded-lg border border-border bg-bg-1 p-4">
      <div className="flex items-center gap-2 mb-2">
        <Icon size={14} style={{ color }} />
        <span className="text-t-3 text-xs">{label}</span>
      </div>
      <span
        className="text-t-1 font-semibold"
        style={{ fontSize: 28, lineHeight: 1 }}
      >
        {value}
      </span>
    </div>
  );
}

function formatTime(ts: number): string {
  return new Date(ts).toLocaleTimeString("en-US", {
    hour12: false,
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function formatDuration(ms: number | null): string {
  if (ms === null) return "...";
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

export function Dashboard({
  solutionName,
  solutionPrefix,
  solutionVersion,
  tables,
  relationships,
  entries,
}: DashboardProps) {
  const fieldCount = tables.reduce((sum, t) => sum + t.fields.length, 0);
  const recentEntries = entries.slice(-5).reverse();

  return (
    <div className="h-full overflow-y-auto p-6">
      {/* Solution header */}
      <div className="mb-6">
        <div className="flex items-center gap-3">
          <h1 className="text-t-1 text-xl font-semibold">
            {solutionName || "No Solution Selected"}
          </h1>
          {tables.length > 0 && <Pill label="Active" color="green" />}
        </div>
        {solutionPrefix && (
          <span
            className="text-t-3 text-xs mt-1 block"
            style={{ fontFamily: "var(--font-geist-mono), monospace" }}
          >
            {solutionPrefix}_ · v{solutionVersion || "0.0.0"}
          </span>
        )}
      </div>

      {/* Stat cards grid */}
      <div className="grid grid-cols-3 gap-3 mb-6">
        <StatCard
          icon={Database}
          label="Tables"
          value={tables.length}
          color="var(--blue)"
        />
        <StatCard
          icon={Columns3}
          label="Fields"
          value={fieldCount}
          color="var(--green)"
        />
        <StatCard
          icon={Link2}
          label="Relationships"
          value={relationships.length}
          color="var(--purple)"
        />
      </div>

      {/* Tables component grid */}
      {tables.length > 0 && (
        <div className="mb-6">
          <h2 className="text-t-2 text-sm font-medium mb-3">Tables</h2>
          <div className="grid grid-cols-2 gap-3">
            {tables.map((table) => (
              <div
                key={table.logicalName}
                className="rounded-lg border border-border bg-bg-1 p-3"
              >
                <div className="flex items-center justify-between mb-1">
                  <span className="text-t-1 text-sm font-medium">
                    {table.displayName}
                  </span>
                  <Pill
                    label={`${table.fields.length} fields`}
                    color="neutral"
                  />
                </div>
                <span
                  className="text-t-3 text-xs"
                  style={{ fontFamily: "var(--font-geist-mono), monospace" }}
                >
                  {table.logicalName}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Recent Activity */}
      <div>
        <h2 className="text-t-2 text-sm font-medium mb-3">Recent Activity</h2>
        {recentEntries.length === 0 ? (
          <div className="text-t-4 text-xs py-4 text-center">
            No activity yet
          </div>
        ) : (
          <div className="rounded-lg border border-border overflow-hidden">
            <table className="w-full text-xs">
              <thead>
                <tr className="bg-bg-2 text-t-3">
                  <th className="text-left px-3 py-2 font-medium">Time</th>
                  <th className="text-left px-3 py-2 font-medium">Agent</th>
                  <th className="text-left px-3 py-2 font-medium">Tool</th>
                  <th className="text-right px-3 py-2 font-medium">Duration</th>
                  <th className="text-center px-3 py-2 font-medium">Status</th>
                </tr>
              </thead>
              <tbody>
                {recentEntries.map((entry, i) => (
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
                      <div className="flex items-center gap-1.5">
                        <AgentDot agentName={entry.agent} size={16} />
                        <span className="text-t-2">{entry.agent}</span>
                      </div>
                    </td>
                    <td className="px-3 py-2">
                      <div className="flex items-center gap-1">
                        <Terminal size={11} className="text-t-4" />
                        <span
                          className="text-t-2"
                          style={{
                            fontFamily: "var(--font-geist-mono), monospace",
                          }}
                        >
                          {entry.toolName}
                        </span>
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
                        <CheckCircle2 size={14} className="text-green mx-auto" />
                      ) : (
                        <Clock size={14} className="text-orange mx-auto animate-pulse" />
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
