import { describe, it, expect } from "vitest";
import {
  classifyApprovalToolCall,
  parseApprovalToolCall,
} from "./approvalParser";
import type { ToolCallInfo } from "./sse-types";

function tc(partial: Partial<ToolCallInfo> & Pick<ToolCallInfo, "id" | "name">): ToolCallInfo {
  return {
    args: "",
    status: "complete",
    startTime: 0,
    endTime: 100,
    messageId: undefined,
    agent: undefined,
    ...partial,
  };
}

describe("classifyApprovalToolCall", () => {
  it("recognizes all single-approval naming variants", () => {
    expect(classifyApprovalToolCall("request_approval")).toBe("single");
    expect(classifyApprovalToolCall("RequestApproval")).toBe("single");
    expect(classifyApprovalToolCall("RequestApprovalAsync")).toBe("single");
  });

  it("recognizes all batch-approval naming variants", () => {
    expect(classifyApprovalToolCall("request_batch_approval")).toBe("batch");
    expect(classifyApprovalToolCall("RequestBatchApproval")).toBe("batch");
    expect(classifyApprovalToolCall("RequestBatchApprovalAsync")).toBe("batch");
  });

  it("returns null for non-approval tool names", () => {
    expect(classifyApprovalToolCall("ExecuteCreateTable")).toBeNull();
    expect(classifyApprovalToolCall("HandoffToTester")).toBeNull();
    expect(classifyApprovalToolCall("")).toBeNull();
  });
});

describe("parseApprovalToolCall — single approval", () => {
  it("returns a populated ApprovalRequest from a finished single-approval call", () => {
    const call = tc({
      id: "tc-1",
      name: "RequestApproval",
      agent: "DataModelBuilder",
      args: JSON.stringify({
        operationType: "create_table",
        description: "Create adc_phone",
        riskLevel: "orange",
        details: "Schema name adc_phone, owner User",
      }),
    });

    const result = parseApprovalToolCall(call);

    expect(result).toEqual({
      toolCallId: "tc-1",
      operationType: "create_table",
      description: "Create adc_phone",
      riskLevel: "orange",
      details: "Schema name adc_phone, owner User",
      agentName: "DataModelBuilder",
    });
  });

  it("defaults missing risk level to 'orange' and missing string fields to ''", () => {
    const result = parseApprovalToolCall(
      tc({ id: "x", name: "RequestApproval", args: "{}" }),
    );
    expect(result?.riskLevel).toBe("orange");
    expect(result?.operationType).toBe("");
    expect(result?.description).toBe("");
    expect(result?.details).toBe("");
  });

  it("returns null when args is not valid JSON yet (mid-stream)", () => {
    expect(parseApprovalToolCall(tc({ id: "x", name: "RequestApproval", args: '{"op' })))
      .toBeNull();
  });
});

describe("parseApprovalToolCall — batch approval", () => {
  it("parses items from the JSON-encoded inner string", () => {
    const inner = JSON.stringify([
      { planId: "p1", summary: "Create field A", riskLevel: "yellow" },
      { planId: "p2", summary: "Create field B", riskLevel: "orange" },
    ]);
    const call = tc({
      id: "tc-2",
      name: "RequestBatchApprovalAsync",
      agent: "DataModelBuilder",
      args: JSON.stringify({
        operationType: "batch_create",
        description: "Create 2 fields",
        riskLevel: "orange",
        items: inner,
      }),
    });

    const result = parseApprovalToolCall(call);

    expect(result?.batchItems).toEqual([
      { planId: "p1", summary: "Create field A", riskLevel: "yellow" },
      { planId: "p2", summary: "Create field B", riskLevel: "orange" },
    ]);
    expect(result?.details).toBe("");
    expect(result?.agentName).toBe("DataModelBuilder");
  });

  it("tolerates a missing items field (returns empty batchItems)", () => {
    const call = tc({
      id: "x",
      name: "RequestBatchApproval",
      args: JSON.stringify({ operationType: "batch", description: "" }),
    });
    const result = parseApprovalToolCall(call);
    expect(result?.batchItems).toEqual([]);
  });

  it("tolerates a partially-streamed items string (returns empty batchItems, doesn't throw)", () => {
    // Symptom this protects: TOOL_CALL_ARGS arrives in chunks; the inner items
    // string can be valid outer JSON but invalid inner JSON for one or two ticks.
    const call = tc({
      id: "x",
      name: "RequestBatchApproval",
      args: JSON.stringify({ items: '[{"plan' }),
    });
    const result = parseApprovalToolCall(call);
    expect(result?.batchItems).toEqual([]);
  });

  it("coerces non-string item fields to strings", () => {
    const inner = JSON.stringify([{ planId: 42, summary: null, riskLevel: undefined }]);
    const result = parseApprovalToolCall(
      tc({ id: "x", name: "RequestBatchApproval", args: JSON.stringify({ items: inner }) }),
    );
    expect(result?.batchItems?.[0]).toEqual({
      planId: "42",
      summary: "",
      riskLevel: "orange",
    });
  });
});

describe("parseApprovalToolCall — non-approval", () => {
  it("returns null for unrelated tools", () => {
    const call = tc({ id: "x", name: "ExecuteCreateTable", args: "{}" });
    expect(parseApprovalToolCall(call)).toBeNull();
  });
});
