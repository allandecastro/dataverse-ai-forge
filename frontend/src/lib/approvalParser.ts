/**
 * Pure helpers for turning a completed approval-style tool call into the
 * `ApprovalRequest` payload the UI renders. Extracted from useForgeChat so the
 * variant matching + JSON parsing is unit-testable without spinning up SSE.
 *
 * Naming variants exist because the Microsoft.Extensions.AI SDK strips the
 * `Async` suffix and PascalCases, while older code paths (and prompt examples)
 * use snake_case.
 */

import type { ApprovalRequest, ToolCallInfo } from "@/lib/sse-types";

const SINGLE_NAMES = new Set([
  "request_approval",
  "RequestApproval",
  "RequestApprovalAsync",
]);

const BATCH_NAMES = new Set([
  "request_batch_approval",
  "RequestBatchApproval",
  "RequestBatchApprovalAsync",
]);

export type ApprovalKind = "single" | "batch" | null;

export function classifyApprovalToolCall(name: string): ApprovalKind {
  if (SINGLE_NAMES.has(name)) return "single";
  if (BATCH_NAMES.has(name)) return "batch";
  return null;
}

/**
 * Returns the `ApprovalRequest` for a finished approval tool call, or `null`
 * if the call isn't an approval or if `tc.args` isn't valid JSON yet (the
 * caller can retry on a later chunk).
 */
export function parseApprovalToolCall(tc: ToolCallInfo): ApprovalRequest | null {
  const kind = classifyApprovalToolCall(tc.name);
  if (!kind) return null;

  let args: Record<string, unknown>;
  try {
    args = JSON.parse(tc.args);
  } catch {
    return null;
  }

  const operationType = typeof args.operationType === "string" ? args.operationType : "";
  const description = typeof args.description === "string" ? args.description : "";
  const riskLevel = typeof args.riskLevel === "string" ? args.riskLevel : "orange";

  if (kind === "single") {
    return {
      toolCallId: tc.id,
      operationType,
      description,
      riskLevel,
      details: typeof args.details === "string" ? args.details : "",
      agentName: tc.agent,
    };
  }

  // Batch — `items` arrives as a JSON-encoded string. Tolerate it being missing
  // or partially-streamed; an empty array beats throwing.
  let batchItems: NonNullable<ApprovalRequest["batchItems"]> = [];
  if (typeof args.items === "string" && args.items.trim().length > 0) {
    try {
      const parsed = JSON.parse(args.items);
      if (Array.isArray(parsed)) {
        batchItems = parsed.map((it: Record<string, unknown>) => ({
          planId: String(it?.planId ?? ""),
          summary: String(it?.summary ?? ""),
          riskLevel: String(it?.riskLevel ?? "orange"),
        }));
      }
    } catch {
      // items not valid JSON yet — fine, will retry on next chunk
    }
  }

  return {
    toolCallId: tc.id,
    operationType,
    description,
    riskLevel,
    details: "",
    batchItems,
    agentName: tc.agent,
  };
}
