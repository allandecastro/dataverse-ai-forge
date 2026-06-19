"use client";

import { useState } from "react";
import { ChevronRight, ChevronDown, Terminal, CheckCircle, AlertCircle, Loader, Copy, Check, Maximize2, X } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import { formatDuration } from "@/lib/formatting";
import type { ToolCallInfo } from "@/lib/sse-types";

interface ToolCallsInlineProps {
  toolCalls: ToolCallInfo[];
}

function StatusIcon({ status }: { status: ToolCallInfo["status"] }) {
  if (status === "running") return <Loader size={11} className="text-blue animate-spin" />;
  if (status === "error") return <AlertCircle size={11} className="text-red" />;
  return <CheckCircle size={11} className="text-green" />;
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      onClick={async (e) => {
        e.stopPropagation();
        try {
          await navigator.clipboard.writeText(text);
          setCopied(true);
          setTimeout(() => setCopied(false), 1500);
        } catch {
          // clipboard.writeText can reject (insecure context, denied permission)
          // — silently no-op rather than blow up the tool-call card.
        }
      }}
      title={copied ? "Copied" : "Copy to clipboard"}
      className="flex items-center justify-center rounded p-1 text-t-4 hover:bg-bg-3 hover:text-t-2 transition-colors"
    >
      {copied ? <Check size={11} className="text-green" /> : <Copy size={11} />}
    </button>
  );
}

function ExpandButton({ onClick }: { onClick: () => void }) {
  return (
    <button
      onClick={(e) => {
        e.stopPropagation();
        onClick();
      }}
      title="Expand to full screen"
      className="flex items-center justify-center rounded p-1 text-t-4 hover:bg-bg-3 hover:text-t-2 transition-colors"
    >
      <Maximize2 size={11} />
    </button>
  );
}

function FullscreenViewer({
  title,
  content,
  isError,
  onClose,
}: {
  title: string;
  content: string;
  isError?: boolean;
  onClose: () => void;
}) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-6"
      onClick={onClose}
    >
      <div
        className="w-full max-w-4xl max-h-[85vh] flex flex-col rounded-lg border border-border bg-bg-1 shadow-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex h-12 items-center justify-between border-b border-border px-4">
          <span
            className="text-t-1 text-sm font-medium truncate font-mono"
          >
            {title}
          </span>
          <div className="flex items-center gap-1">
            <CopyButton text={content} />
            <button
              onClick={onClose}
              className="flex items-center justify-center rounded p-1 text-t-3 hover:text-t-1 hover:bg-bg-2 transition-colors"
              title="Close"
            >
              <X size={14} />
            </button>
          </div>
        </div>
        <pre
          className={`flex-1 overflow-auto px-4 py-3 text-xs font-mono ${
            isError ? "text-red" : "text-t-2"
          }`}
        >
          {content}
        </pre>
      </div>
    </div>
  );
}

function ToolCallDetail({ tc }: { tc: ToolCallInfo }) {
  const [expanded, setExpanded] = useState(false);
  const [fullscreen, setFullscreen] = useState<"args" | "result" | null>(null);

  // Pretty-print once per render; cheap for typical results.
  const argsFormatted = tc.args ? formatJson(tc.args) : "";
  const resultFormatted = tc.result ? formatJson(tc.result) : "";

  return (
    <div className="border-b border-border-lt last:border-b-0">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex items-center gap-1.5 w-full px-2 py-1.5 text-xs text-t-2 hover:bg-bg-2 transition-colors"
      >
        {expanded ? <ChevronDown size={11} className="text-t-4" /> : <ChevronRight size={11} className="text-t-4" />}
        <StatusIcon status={tc.status} />
        <span
          className="flex-1 text-left truncate font-mono"
        >
          {tc.name}
        </span>
        <Pill
          label={formatDuration(tc.endTime ? tc.endTime - tc.startTime : null)}
          color={tc.status === "error" ? "red" : tc.endTime ? "green" : "neutral"}
        />
      </button>
      {expanded && (
        <div className="px-2 pb-2 space-y-1">
          {tc.args && (
            <div>
              <div className="flex items-center justify-between">
                <span className="text-t-4 text-xs">Args</span>
                <div className="flex items-center gap-0.5">
                  <CopyButton text={argsFormatted} />
                  <ExpandButton onClick={() => setFullscreen("args")} />
                </div>
              </div>
              <pre
                className="bg-bg-1 rounded px-2 py-1 text-xs text-t-3 overflow-auto max-h-80 font-mono"
              >
                {argsFormatted}
              </pre>
            </div>
          )}
          {tc.result && (
            <div>
              <div className="flex items-center justify-between">
                <span className="text-t-4 text-xs">Result</span>
                <div className="flex items-center gap-0.5">
                  <CopyButton text={resultFormatted} />
                  <ExpandButton onClick={() => setFullscreen("result")} />
                </div>
              </div>
              <pre
                className={`rounded px-2 py-1 text-xs overflow-auto max-h-80 font-mono ${
                  tc.status === "error" ? "bg-red-bg text-red" : "bg-bg-1 text-t-3"
                }`}
              >
                {resultFormatted}
              </pre>
            </div>
          )}
        </div>
      )}
      {fullscreen === "args" && (
        <FullscreenViewer
          title={`${tc.name} — Args`}
          content={argsFormatted}
          onClose={() => setFullscreen(null)}
        />
      )}
      {fullscreen === "result" && (
        <FullscreenViewer
          title={`${tc.name} — Result`}
          content={resultFormatted}
          isError={tc.status === "error"}
          onClose={() => setFullscreen(null)}
        />
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
        {runningCount > 0 && <Pill label="running" color="blue" />}
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
