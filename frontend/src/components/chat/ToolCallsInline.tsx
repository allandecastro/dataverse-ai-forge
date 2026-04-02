"use client";

import { useState } from "react";
import { ChevronRight, ChevronDown, Terminal, CheckCircle, AlertCircle, Loader } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import type { ToolCallInfo } from "@/lib/sse-types";

interface ToolCallsInlineProps {
  toolCalls: ToolCallInfo[];
}

function formatDuration(startTime: number, endTime?: number): string {
  if (!endTime) return "...";
  const ms = endTime - startTime;
  if (ms < 1000) return `${ms}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

function StatusIcon({ status }: { status: ToolCallInfo["status"] }) {
  if (status === "running") return <Loader size={11} className="text-orange animate-spin" />;
  if (status === "error") return <AlertCircle size={11} className="text-red" />;
  return <CheckCircle size={11} className="text-green" />;
}

function ToolCallDetail({ tc }: { tc: ToolCallInfo }) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="border-b border-border-lt last:border-b-0">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex items-center gap-1.5 w-full px-2 py-1.5 text-xs text-t-2 hover:bg-bg-2 transition-colors"
      >
        {expanded ? <ChevronDown size={11} className="text-t-4" /> : <ChevronRight size={11} className="text-t-4" />}
        <StatusIcon status={tc.status} />
        <span
          className="flex-1 text-left truncate"
          style={{ fontFamily: "var(--font-geist-mono), monospace" }}
        >
          {tc.name}
        </span>
        <Pill
          label={formatDuration(tc.startTime, tc.endTime)}
          color={tc.status === "error" ? "red" : tc.endTime ? "green" : "neutral"}
        />
      </button>
      {expanded && (
        <div className="px-2 pb-2 space-y-1">
          {tc.args && (
            <div>
              <span className="text-t-4 text-xs">Args</span>
              <pre
                className="bg-bg-1 rounded px-2 py-1 text-xs text-t-3 overflow-x-auto max-h-32"
                style={{ fontFamily: "var(--font-geist-mono), monospace" }}
              >
                {formatJson(tc.args)}
              </pre>
            </div>
          )}
          {tc.result && (
            <div>
              <span className="text-t-4 text-xs">Result</span>
              <pre
                className={`rounded px-2 py-1 text-xs overflow-x-auto max-h-32 ${
                  tc.status === "error" ? "bg-red-bg text-red" : "bg-bg-1 text-t-3"
                }`}
                style={{ fontFamily: "var(--font-geist-mono), monospace" }}
              >
                {formatJson(tc.result)}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function formatJson(str: string): string {
  try {
    return JSON.stringify(JSON.parse(str), null, 2);
  } catch {
    return str;
  }
}

export function ToolCallsInline({ toolCalls }: ToolCallsInlineProps) {
  const [expanded, setExpanded] = useState(false);
  if (toolCalls.length === 0) return null;

  const errorCount = toolCalls.filter((tc) => tc.status === "error").length;
  const runningCount = toolCalls.filter((tc) => tc.status === "running").length;

  return (
    <div className="mt-1 ml-7">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex items-center gap-1.5 text-t-3 text-xs hover:text-t-2 transition-colors"
      >
        {expanded ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
        <Terminal size={11} />
        <span>
          {toolCalls.length} tool call{toolCalls.length !== 1 ? "s" : ""}
        </span>
        {errorCount > 0 && <Pill label={`${errorCount} error`} color="red" />}
        {runningCount > 0 && <Pill label="running" color="orange" />}
      </button>
      {expanded && (
        <div className="mt-1 rounded-md border border-border bg-bg-2 overflow-hidden">
          {toolCalls.map((tc) => (
            <ToolCallDetail key={tc.id} tc={tc} />
          ))}
        </div>
      )}
    </div>
  );
}
