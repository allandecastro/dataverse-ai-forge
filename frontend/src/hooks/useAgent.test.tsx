import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook } from "@testing-library/react";
import { useAgent } from "./useAgent";
import { DEFAULT_AGENT_NAME } from "@/lib/constants";
import type { AgentState } from "@/lib/sse-types";

// Drive the hook off a mocked chat context instead of the full ForgeChatProvider.
let mockCtx: { agentState: AgentState; isStreaming: boolean };
vi.mock("@/hooks/useForgeChat", () => ({
  useForgeChatContext: () => mockCtx,
}));

beforeEach(() => {
  mockCtx = { agentState: { activeAgent: "" }, isStreaming: false };
});

describe("useAgent", () => {
  it("falls back to the default agent when none is active (null before first STATE_DELTA)", () => {
    // activeAgent is null until the server sends the first agent; the hook uses
    // `?? DEFAULT_AGENT_NAME` (nullish, so only null/undefined trigger it).
    mockCtx = { agentState: { activeAgent: null as unknown as string }, isStreaming: false };
    const { result } = renderHook(() => useAgent());
    expect(result.current.currentAgent).toBe(DEFAULT_AGENT_NAME);
    expect(result.current.phase).toBe("design"); // SolutionArchitect → design
  });

  it("maps known agents to their phase and reflects streaming state", () => {
    mockCtx = { agentState: { activeAgent: "DataModelBuilder" }, isStreaming: true };
    const { result } = renderHook(() => useAgent());
    expect(result.current.currentAgent).toBe("DataModelBuilder");
    expect(result.current.phase).toBe("building");
    expect(result.current.running).toBe(true);
  });

  it("maps an unknown agent to the idle phase", () => {
    mockCtx = { agentState: { activeAgent: "TotallyUnknownAgent" }, isStreaming: false };
    const { result } = renderHook(() => useAgent());
    expect(result.current.currentAgent).toBe("TotallyUnknownAgent");
    expect(result.current.phase).toBe("idle");
  });

  it("passes solution-state fields through", () => {
    mockCtx = {
      agentState: {
        activeAgent: "Tester",
        solutionState: {
          environmentName: "Contoso",
          environmentUrl: "https://contoso.crm.dynamics.com",
          solutionName: "Feedback",
          solutionPrefix: "adc",
          solutionVersion: "1.0.0.0",
        },
      },
      isStreaming: false,
    };
    const { result } = renderHook(() => useAgent());
    expect(result.current.phase).toBe("testing");
    expect(result.current.environmentName).toBe("Contoso");
    expect(result.current.solutionPrefix).toBe("adc");
    expect(result.current.solutionVersion).toBe("1.0.0.0");
  });

  it("defaults solution-state fields to empty strings when absent", () => {
    mockCtx = { agentState: { activeAgent: "Tester" }, isStreaming: false };
    const { result } = renderHook(() => useAgent());
    expect(result.current.environmentName).toBe("");
    expect(result.current.solutionName).toBe("");
    expect(result.current.solutionPrefix).toBe("");
  });
});
