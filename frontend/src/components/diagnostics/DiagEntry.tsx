"use client";

import { useState } from "react";
import { Terminal, ChevronRight, ChevronDown } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { Pill } from "@/components/primitives/Pill";
import { CodeBlock } from "./CodeBlock";
import { agents, defaultAgent } from "@/lib/agents";
import type { DiagnosticEntry } from "@/lib/types";

interface DiagEntryProps {
  entry: DiagnosticEntry;
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

function durationColor(ms: number | null): "green" | "orange" | "neutral" {
  if (ms === null) return "neutral";
  return ms <= 1000 ? "green" : "orange";
}

export function DiagEntry({ entry }: DiagEntryProps) {
  const [payloadOpen, setPayloadOpen] = useState(false);
  const [resultOpen, setResultOpen] = useState(false);
  const agent = agents[entry.agent] ?? defaultAgent;

  return (
    <div className="border-b border-border-lt py-2 px-3" data-entry-id={entry.id}>
      {/* Header row */}
      <div className="flex items-center gap-2">
        <AgentDot agentName={entry.agent} size={18} />
        <span className="text-t-2 text-xs font-medium flex-1">
          {agent.displayName}
        </span>
        <span
          className="text-t-3 text-xs"
          style={{ fontFamily: "var(--font-geist-mono), monospace" }}
        >
          {formatTime(entry.timestamp)}
        </span>
        <Pill
          label={formatDuration(entry.duration)}
          color={durationColor(entry.duration)}
        />
      </div>

      {/* Tool row */}
      <div className="flex items-center gap-1.5 mt-1 ml-[26px]">
        <Terminal size={11} className="text-t-4" />
        <span
          className="text-t-2 text-xs"
          style={{ fontFamily: "var(--font-geist-mono), monospace" }}
        >
          {entry.toolName}
        </span>
        {entry.status === "running" && (
          <div className="flex gap-0.5 items-center ml-1">
            {[0, 1, 2].map((d) => (
              <span
                key={d}
                className="block w-1 h-1 rounded-full bg-orange"
                style={{
                  animation: "streaming-dots 1.4s infinite ease-in-out both",
                  animationDelay: `${d * 0.16}s`,
                }}
              />
            ))}
          </div>
        )}
      </div>

      {/* Collapsible Payload */}
      {entry.payload != null && (
        <div className="ml-[26px] mt-1">
          <button
            onClick={() => setPayloadOpen(!payloadOpen)}
            className="flex items-center gap-1 text-t-3 text-xs hover:text-t-2"
          >
            {payloadOpen ? (
              <ChevronDown size={12} />
            ) : (
              <ChevronRight size={12} />
            )}
            Payload
          </button>
          {payloadOpen && (
            <div className="mt-1">
              <CodeBlock data={entry.payload} />
            </div>
          )}
        </div>
      )}

      {/* Collapsible Result */}
      {entry.result != null && (
        <div className="ml-[26px] mt-1">
          <button
            onClick={() => setResultOpen(!resultOpen)}
            className="flex items-center gap-1 text-t-3 text-xs hover:text-t-2"
          >
            {resultOpen ? (
              <ChevronDown size={12} />
            ) : (
              <ChevronRight size={12} />
            )}
            Result
          </button>
          {resultOpen && (
            <div className="mt-1">
              <CodeBlock data={entry.result} />
            </div>
          )}
        </div>
      )}
    </div>
  );
}
