"use client";

import { useForgeChatContext } from "@/hooks/useForgeChat";
import { ApprovalCard } from "@/components/chat/ApprovalCard";
import type { RiskLevel, ApprovalStatus } from "@/lib/types";
import type { ReactNode } from "react";

/**
 * Reads pendingApproval from the chat context and renders an ApprovalCard.
 * On user decision, sends a structured message back via sendMessage().
 */
export function useForgeApprovals(): { approvalElement: ReactNode | null } {
  const { pendingApproval, clearApproval, sendMessage } = useForgeChatContext();

  if (!pendingApproval) {
    return { approvalElement: null };
  }

  const { operationType, description, riskLevel, details } = pendingApproval;
  const cardStatus: ApprovalStatus = "pending";

  const approvalElement = (
    <ApprovalCard
      operationType={operationType}
      description={description}
      riskLevel={riskLevel as RiskLevel}
      details={details}
      status={cardStatus}
      onApprove={() => {
        clearApproval();
        sendMessage(`APPROVED: ${operationType} — User approved the operation.`);
      }}
      onEdit={() => {
        clearApproval();
        sendMessage(`EDIT: ${operationType} — User wants to edit the operation. Ask what they want to change.`);
      }}
      onReject={() => {
        clearApproval();
        sendMessage(`REJECTED: ${operationType} — User rejected the operation. Do NOT proceed.`);
      }}
    />
  );

  return { approvalElement };
}
