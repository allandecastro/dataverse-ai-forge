"use client";

import { useState, useMemo, useId } from "react";
import { Shield, ChevronRight, ChevronDown, Check, X, Code } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import { CodeBlock } from "@/components/diagnostics/CodeBlock";
import { BpfVisualizer } from "@/components/workspace/BpfVisualizer";
import type { RiskLevel, ApprovalStatus } from "@/lib/types";

interface CodeTab {
  label: string;
  code: string;
}

interface ApprovalCardProps {
  operationType: string;
  description: string;
  riskLevel: RiskLevel;
  details: string;
  status: ApprovalStatus;
  onApprove?: () => void;
  onEdit?: () => void;
  onReject?: () => void;
  /** When set, render a batch list instead of the single-operation details. */
  batchItems?: Array<{ planId: string; summary: string; riskLevel: string }>;
  /** Agent that requested approval (e.g. "DataModelBuilder"). Shown in the header. */
  agentName?: string;
}

const riskColors: Record<RiskLevel, string> = {
  green: "var(--green)",
  yellow: "var(--orange)",
  orange: "var(--orange)",
  red: "var(--red)",
};

const riskLabels: Record<RiskLevel, string> = {
  green: "Low",
  yellow: "Medium",
  orange: "High",
  red: "Critical",
};

const riskPillColor: Record<RiskLevel, "green" | "yellow" | "orange" | "red"> = {
  green: "green",
  yellow: "yellow",
  orange: "orange",
  red: "red",
};

function parseDetails(details: string): unknown {
  try {
    return JSON.parse(details);
  } catch {
    return details;
  }
}

function extractBpfDiagram(opType: string, details: string): string | null {
  if (!opType.toLowerCase().includes("bpf")) {
    // Check details for stage flow pattern
    if (!details.includes("→") || !details.includes("[")) return null;
  }
  try {
    const parsed = JSON.parse(details);
    if (typeof parsed === "object" && parsed !== null && typeof parsed.stageFlowDiagram === "string") {
      return parsed.stageFlowDiagram;
    }
  } catch {
    // Try raw string match for [Stage] → [Stage] pattern
    const match = details.match(/\[[\w\s]+(?::[\w\s*,]+)?\](?:\s*→\s*\[[\w\s]+(?::[\w\s*,]+)?\])+/);
    if (match) return match[0];
  }
  return null;
}

function extractCodeTabs(details: string): CodeTab[] {
  const tabs: CodeTab[] = [];
  try {
    const parsed = JSON.parse(details);
    if (typeof parsed !== "object" || parsed === null) return tabs;

    if (parsed.csharpCode && typeof parsed.csharpCode === "string") {
      tabs.push({ label: "Plugin Code", code: parsed.csharpCode });
    }
    if (parsed.unitTestCode && typeof parsed.unitTestCode === "string") {
      tabs.push({ label: "Unit Tests", code: parsed.unitTestCode });
    }
    if (parsed.registrationSteps) {
      const text =
        typeof parsed.registrationSteps === "string"
          ? parsed.registrationSteps
          : JSON.stringify(parsed.registrationSteps, null, 2);
      tabs.push({ label: "Registration", code: text });
    }
    if (parsed.projectStructure) {
      const text =
        typeof parsed.projectStructure === "string"
          ? parsed.projectStructure
          : JSON.stringify(parsed.projectStructure, null, 2);
      tabs.push({ label: "Project", code: text });
    }
  } catch {
    // Not JSON or no code fields
  }
  return tabs;
}

export function ApprovalCard({
  operationType,
  description,
  riskLevel,
  details,
  status,
  onApprove,
  onEdit,
  onReject,
  batchItems,
  agentName,
}: ApprovalCardProps) {
  const isBatch = Array.isArray(batchItems) && batchItems.length > 0;
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [codeOpen, setCodeOpen] = useState(false);
  const [activeTab, setActiveTab] = useState(0);

  // React 19 useId — one stable base per card, suffixed for each region
  // so aria-controls / aria-labelledby pairs never collide across cards.
  const uid = useId();
  const titleId = `${uid}-title`;
  const detailsPanelId = `${uid}-details`;
  const codePanelId = `${uid}-code`;

  const codeTabs = useMemo(() => extractCodeTabs(details), [details]);
  const bpfDiagram = useMemo(() => extractBpfDiagram(operationType, details), [operationType, details]);

  // aria-labelledby points at the visible heading. Per ARIA spec
  // labelledby wins over label when both are set, so we use only
  // labelledby — SR users hear what sighted users see, and operation
  // type + risk are announced naturally as the reader moves through
  // the body of the card.
  return (
    <div
      role="group"
      aria-labelledby={titleId}
      className="rounded-lg bg-bg-2 overflow-hidden"
      style={{ borderLeft: `3px solid ${riskColors[riskLevel]}` }}
    >
      {/* Header */}
      <div className="flex items-center gap-2 px-3 py-2">
        <Shield size={14} aria-hidden="true" style={{ color: riskColors[riskLevel] }} />
        <span id={titleId} className="text-t-1 text-sm font-medium flex-1">
          {isBatch ? `Batch Approval — ${batchItems!.length} operations` : "Approval Required"}
          {agentName && (
            <span className="text-t-3 text-xs font-normal ml-1.5">
              · from {agentName}
            </span>
          )}
        </span>
        <Pill label={riskLabels[riskLevel]} color={riskPillColor[riskLevel]} />
      </div>

      {/* Body */}
      <div className="px-3 pb-2 space-y-1.5">
        <span
          className="text-t-2 text-xs block"
          style={{ fontFamily: "var(--font-geist-mono), monospace" }}
        >
          {operationType}
        </span>
        <p className="text-t-2 text-sm leading-relaxed">{description}</p>
      </div>

      {/* Batch items list */}
      {isBatch && (
        <div className="px-3 pb-2">
          <ol className="space-y-1 text-t-2 text-xs">
            {batchItems!.map((it, i) => (
              <li key={`${it.planId}-${i}`} className="flex items-start gap-2">
                <span className="text-t-3 tabular-nums">{i + 1}.</span>
                <span className="flex-1">{it.summary || it.planId}</span>
              </li>
            ))}
          </ol>
        </div>
      )}

      {/* Collapsible details */}
      {!isBatch && details && (
        <div className="px-3 pb-2">
          <button
            type="button"
            aria-expanded={detailsOpen}
            aria-controls={detailsPanelId}
            onClick={() => setDetailsOpen(!detailsOpen)}
            className="flex items-center gap-1 text-t-3 text-xs hover:text-t-2"
          >
            {detailsOpen ? (
              <ChevronDown size={12} aria-hidden="true" />
            ) : (
              <ChevronRight size={12} aria-hidden="true" />
            )}
            Details
          </button>
          {detailsOpen && (
            <div id={detailsPanelId} className="mt-1">
              <CodeBlock data={parseDetails(details)} maxHeight={200} />
            </div>
          )}
        </div>
      )}

      {/* BPF stage visualizer */}
      {bpfDiagram && (
        <div className="px-3 pb-2">
          <BpfVisualizer stageFlowDiagram={bpfDiagram} />
        </div>
      )}

      {/* Code preview (for plugin operations) */}
      {codeTabs.length > 0 && (
        <div className="px-3 pb-2">
          <button
            type="button"
            aria-expanded={codeOpen}
            aria-controls={codePanelId}
            onClick={() => setCodeOpen(!codeOpen)}
            className="flex items-center gap-1 text-t-3 text-xs hover:text-t-2"
          >
            {codeOpen ? (
              <ChevronDown size={12} aria-hidden="true" />
            ) : (
              <Code size={12} aria-hidden="true" />
            )}
            Code Preview
          </button>
          {codeOpen && (
            <div id={codePanelId} className="mt-1 space-y-1">
              <div role="tablist" aria-label="Code views" className="flex gap-1">
                {codeTabs.map((tab, i) => {
                  const tabId = `${uid}-tab-${i}`;
                  const panelId = `${uid}-tabpanel-${i}`;
                  const selected = activeTab === i;
                  return (
                    <button
                      key={tab.label}
                      type="button"
                      role="tab"
                      id={tabId}
                      aria-selected={selected}
                      aria-controls={panelId}
                      // Roving tabindex: only the selected tab is in the tab order,
                      // the others are reachable via arrow keys (browser default).
                      tabIndex={selected ? 0 : -1}
                      onClick={() => setActiveTab(i)}
                      className={`rounded px-2 py-0.5 text-xs transition-colors ${
                        selected
                          ? "bg-code-bg text-t-1"
                          : "text-t-3 hover:text-t-2"
                      }`}
                      style={{ fontFamily: "var(--font-geist-mono), monospace" }}
                    >
                      {tab.label}
                    </button>
                  );
                })}
              </div>
              <div
                role="tabpanel"
                id={`${uid}-tabpanel-${activeTab}`}
                aria-labelledby={`${uid}-tab-${activeTab}`}
              >
                <CodeBlock
                  data={codeTabs[activeTab]?.code ?? ""}
                  maxHeight={280}
                />
              </div>
            </div>
          )}
        </div>
      )}

      {/* Action row or result badge */}
      <div className="px-3 pb-3">
        {status === "pending" ? (
          <div className="flex items-center gap-2">
            <button
              type="button"
              aria-label={`Approve ${operationType}`}
              onClick={onApprove}
              className="flex items-center gap-1 rounded px-3 py-1.5 text-xs font-medium bg-green-bg text-green hover:opacity-80 transition-opacity"
            >
              <Check size={12} aria-hidden="true" />
              Approve
            </button>
            <button
              type="button"
              aria-label={`Edit ${operationType} before approving`}
              onClick={onEdit}
              className="flex items-center gap-1 rounded px-3 py-1.5 text-xs font-medium text-blue hover:opacity-80 transition-opacity"
            >
              Edit
            </button>
            <button
              type="button"
              aria-label={`Reject ${operationType}`}
              onClick={onReject}
              className="flex items-center gap-1 rounded px-3 py-1.5 text-xs font-medium text-red hover:opacity-80 transition-opacity"
            >
              <X size={12} aria-hidden="true" />
              Reject
            </button>
          </div>
        ) : (
          <Pill
            label={status === "approved" ? "Approved" : "Rejected"}
            color={status === "approved" ? "green" : "red"}
          />
        )}
      </div>
    </div>
  );
}
