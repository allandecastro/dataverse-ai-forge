import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useDiagnostics } from "./useDiagnostics";
import type { ToolCallInfo } from "@/lib/sse-types";

// The hook derives entries from toolCalls (chat context) and toasts on errors.
let mockToolCalls: ToolCallInfo[];
const addToast = vi.fn();

vi.mock("@/hooks/useForgeChat", () => ({
  useForgeChatContext: () => ({ toolCalls: mockToolCalls }),
}));
vi.mock("@/contexts/ToastContext", () => ({
  useToast: () => ({ addToast }),
}));

beforeEach(() => {
  mockToolCalls = [];
  addToast.mockReset();
});

describe("useDiagnostics", () => {
  it("derives a completed entry: duration, parsed payload/result, complete status", () => {
    mockToolCalls = [
      {
        id: "1",
        name: "PlanCreateTableAsync",
        args: '{"x":1}',
        result: '"done"',
        status: "complete",
        startTime: 1000,
        endTime: 1500,
        agent: "DataModelBuilder",
      },
    ];
    const { result } = renderHook(() => useDiagnostics());
    const e = result.current.entries[0];
    expect(e).toMatchObject({
      id: "1",
      agent: "DataModelBuilder", // server-recorded agent wins
      toolName: "PlanCreateTableAsync",
      timestamp: 1000,
      duration: 500,
      status: "complete",
    });
    expect(e.payload).toEqual({ x: 1 });
    expect(e.result).toBe("done");
    expect(addToast).not.toHaveBeenCalled();
  });

  it("flags an error result (regex on 'error:') and toasts once", () => {
    mockToolCalls = [
      {
        id: "2",
        name: "ExecuteCreateFieldAsync",
        args: "{}",
        result: "error: boom",
        status: "complete", // status says complete, but the result text is an error
        startTime: 0,
      },
    ];
    const { result, rerender } = renderHook(() => useDiagnostics());
    expect(result.current.entries[0].status).toBe("error");
    expect(result.current.entries[0].duration).toBeNull(); // no endTime

    expect(addToast).toHaveBeenCalledTimes(1);
    expect(addToast).toHaveBeenCalledWith(expect.stringContaining("failed"), "error");

    // Re-render with a fresh array of the same error id → no duplicate toast.
    mockToolCalls = [...mockToolCalls];
    rerender();
    expect(addToast).toHaveBeenCalledTimes(1);
  });

  it("resolves the agent from the static map when the server didn't record one", () => {
    mockToolCalls = [
      { id: "3", name: "VerifyTableCreationAsync", args: "{}", status: "running", startTime: 0 },
      { id: "4", name: "SomethingUnmapped", args: "{}", status: "running", startTime: 0 },
    ];
    const { result } = renderHook(() => useDiagnostics());
    expect(result.current.entries[0].agent).toBe("Tester"); // from toolToAgent
    expect(result.current.entries[1].agent).toBe("SolutionArchitect"); // default fallback
    expect(result.current.entries[0].status).toBe("running");
  });
});
