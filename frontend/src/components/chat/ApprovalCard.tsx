"use client";

import { useState, useMemo } from "react";
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
}: ApprovalCardProps) {
  const [detailsOpen, setDetailsOpen] = useState(false);
  const [codeOpen, setCodeOpen] = useState(false);
  const [activeTab, setActiveTab] = useState(0);

  const codeTabs = useMemo(() => extractCodeTabs(details), [details]);
  const bpfDiagram = useMemo(() => extractBpfDiagram(operationType, details), [operationType, details]);

  return (
    <div
      className="rounded-lg bg-bg-2 overflow-hidden"
      style={{ borderLeft: `3px solid ${riskColors[riskLevel]}` }}
    >
      {/* Header */}
      <div className="flex items-center gap-2 px-3 py-2">
        <Shield size={14} style={{ color: riskColors[riskLevel] }} />
        <span className="text-t-1 text-sm font-medium flex-1">
          Approval Required
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

      {/* Collapsible details */}
      {details && (
        <div className="px-3 pb-2">
          <button
            onClick={() => setDetailsOpen(!detailsOpen)}
            className="flex items-center gap-1 text-t-3 text-xs hover:text-t-2"
          >
            {detailsOpen ? (
              <ChevronDown size={12} />
            ) : (
              <ChevronRight size={12} />
            )}
            Details
          </button>
          {detailsOpen && (
            <div className="mt-1">
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
            onClick={() => setCodeOpen(!codeOpen)}
            className="flex items-center gap-1 text-t-3 text-xs hover:text-t-2"
          >
            {codeOpen ? (
              <ChevronDown size={12} />
            ) : (
              <Code size={12} />
            )}
            Code Preview
          </button>
          {codeOpen && (
            <div className="mt-1 space-y-1">
              <div className="flex gap-1">
                {codeTabs.map((tab, i) => (
                  <button
                    key={tab.label}
                    onClick={() => setActiveTab(i)}
                    className={`rounded px-2 py-0.5 text-xs transition-colors ${
                      activeTab === i
                        ? "bg-code-bg text-t-1"
                        : "text-t-3 hover:text-t-2"
                    }`}
                    style={{ fontFamily: "var(--font-geist-mono), monospace" }}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
              <CodeBlock
                data={codeTabs[activeTab]?.code ?? ""}
                maxHeight={280}
              />
            </div>
          )}
        </div>
      )}

      {/* Action row or result badge */}
      <div className="px-3 pb-3">
        {status === "pending" ? (
          <div className="flex items-center gap-2">
            <button
              onClick={onApprove}
              className="flex items-center gap-1 rounded px-3 py-1.5 text-xs font-medium bg-green-bg text-green hover:opacity-80 transition-opacity"
            >
              <Check size={12} />
              Approve
            </button>
            <button
              onClick={onEdit}
              className="flex items-center gap-1 rounded px-3 py-1.5 text-xs font-medium text-blue hover:opacity-80 transition-opacity"
            >
              Edit
            </button>
            <button
              onClick={onReject}
              className="flex items-center gap-1 rounded px-3 py-1.5 text-xs font-medium text-red hover:opacity-80 transition-opacity"
            >
              <X size={12} />
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
