"use client";

import { useState, useMemo, useId } from "react";
import { Shield, ChevronRight, ChevronDown, Check, X, Code, AlertTriangle } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import { Button } from "@/components/primitives/Button";
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
  // High-risk ops open Details by default — for a red/orange operation the user
  // should see what actually changes before approving, not description-only.
  const [detailsOpen, setDetailsOpen] = useState(riskLevel === "red" || riskLevel === "orange");
  const [codeOpen, setCodeOpen] = useState(false);
  const [activeTab, setActiveTab] = useState(0);
  // Critical (red) operations require a second click to approve — parity with
  // the ConfirmDialog guarding session-clear. One misclick shouldn't fire an
  // irreversible Dataverse write.
  const requiresConfirm = riskLevel === "red";
  const [confirmingApprove, setConfirmingApprove] = useState(false);

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
      className="border border-border bg-bg-1 overflow-hidden"
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
          className="text-t-2 text-xs block font-mono"
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
                      className={`rounded px-2 py-0.5 text-xs transition-colors font-mono ${
                        selected
                          ? "bg-code-bg text-t-1"
                          : "text-t-3 hover:text-t-2"
                      }`}
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
          <div className="space-y-1.5">
            {confirmingApprove && (
              <p className="flex items-center gap-1 text-red text-xs">
                <AlertTriangle size={12} aria-hidden="true" />
                Critical operation — this can&apos;t be undone. Click Confirm to proceed.
              </p>
            )}
            <div className="flex items-center gap-2">
              <Button
                variant="primary"
                aria-label={
                  confirmingApprove
                    ? `Confirm ${operationType} — irreversible`
                    : `Approve ${operationType}`
                }
                onClick={() => {
                  // Two-step for Critical: first click arms, second click applies.
                  if (requiresConfirm && !confirmingApprove) {
                    setConfirmingApprove(true);
                    return;
                  }
                  onApprove?.();
                }}
              >
                <Check size={13} aria-hidden="true" />
                {confirmingApprove ? "Confirm — can't be undone" : "Approve & apply"}
              </Button>
              <Button
                variant="secondary"
                aria-label={`Request changes to ${operationType}`}
                onClick={onEdit}
              >
                Request changes
              </Button>
              <Button
                variant="ghostDanger"
                aria-label={`Reject ${operationType}`}
                onClick={onReject}
                className="ml-auto"
              >
                <X size={12} aria-hidden="true" />
                Reject
              </Button>
            </div>
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
