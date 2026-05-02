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

  const { operationType, description, riskLevel, details, batchItems, agentName } = pendingApproval;
  const cardStatus: ApprovalStatus = "pending";
  const isBatch = Array.isArray(batchItems) && batchItems.length > 0;

  const approvalElement = (
    <ApprovalCard
      operationType={operationType}
      description={description}
      riskLevel={riskLevel as RiskLevel}
      details={details}
      status={cardStatus}
      batchItems={batchItems}
      agentName={agentName}
      onApprove={() => {
        clearApproval();
        // Single short message — no imperatives, no GUIDs, no count.
        // Past traces showed AOAI's content_filter flagging variants like
        // "User approved all 4 operations. Execute them." — the imperative
        // pattern + dense planIds tripped the jailbreak classifier. The
        // agent has the planIds in its earlier tool result and knows to
        // continue executing without being told to.
        sendMessage("User approved.", { kind: "approval-receipt" });
      }}
      onEdit={() => {
        clearApproval();
        sendMessage("User wants to edit.", { kind: "approval-edit" });
      }}
      onReject={() => {
        clearApproval();
        sendMessage("User rejected.", { kind: "approval-rejection" });
      }}
    />
  );

  return { approvalElement };
}
