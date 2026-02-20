"use client";

import { useState } from "react";
import { Shield, ChevronRight, ChevronDown, Check, X } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import { CodeBlock } from "@/components/diagnostics/CodeBlock";
import type { RiskLevel, ApprovalStatus } from "@/lib/types";

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
