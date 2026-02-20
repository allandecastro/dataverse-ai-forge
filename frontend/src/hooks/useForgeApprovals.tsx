"use client";

import { useCopilotAction } from "@copilotkit/react-core";
import { ApprovalCard } from "@/components/chat/ApprovalCard";
import type { RiskLevel, ApprovalStatus } from "@/lib/types";

export function useForgeApprovals(): void {
  useCopilotAction({
    name: "request_approval",
    description:
      "Request user approval before executing a Dataverse write operation",
    parameters: [
      {
        name: "operationType",
        type: "string",
        description:
          "The operation being requested (e.g. CreateTable, CreateField)",
        required: true,
      },
      {
        name: "description",
        type: "string",
        description: "Human-readable description of what will happen",
        required: true,
      },
      {
        name: "riskLevel",
        type: "string",
        description:
          "Risk classification: green (low), yellow (medium), orange (high), red (critical)",
        required: true,
      },
      {
        name: "details",
        type: "string",
        description: "JSON payload of the planned operation",
        required: true,
      },
    ],
    renderAndWaitForResponse: ({ args, respond, status }) => {
      const operationType = (args.operationType as string) ?? "";
      const description = (args.description as string) ?? "";
      const riskLevel = ((args.riskLevel as string) ?? "orange") as RiskLevel;
      const details = (args.details as string) ?? "";

      if (status === "inProgress") {
        return (
          <div className="flex gap-2 items-center text-t-3 text-xs py-2">
            <div className="w-1.5 h-1.5 rounded-full bg-orange animate-pulse" />
            <span>Preparing approval request...</span>
          </div>
        );
      }

      const cardStatus: ApprovalStatus =
        status === "complete" ? "approved" : "pending";

      return (
        <ApprovalCard
          operationType={operationType}
          description={description}
          riskLevel={riskLevel}
          details={details}
          status={cardStatus}
          onApprove={() =>
            respond?.({
              decision: "approved",
              message: "User approved the operation.",
            })
          }
          onEdit={() =>
            respond?.({
              decision: "edit",
              message:
                "User wants to edit the operation. Ask what they want to change.",
            })
          }
          onReject={() =>
            respond?.({
              decision: "rejected",
              message: "User rejected the operation. Do NOT proceed.",
            })
          }
        />
      );
    },
  });
}
