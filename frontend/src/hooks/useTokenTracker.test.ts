import { describe, it, expect } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useTokenTracker } from "./useTokenTracker";

describe("useTokenTracker", () => {
  it("starts empty", () => {
    const { result } = renderHook(() => useTokenTracker());
    expect(result.current.state.entries).toEqual([]);
    expect(result.current.state.totalTokens).toBe(0);
  });

  it("accumulates usage across turns and computes per-entry + running totals", () => {
    const { result } = renderHook(() => useTokenTracker());

    act(() => result.current.addUsage("SolutionArchitect", 100, 50));
    act(() => result.current.addUsage("DataModelBuilder", 200, 25));

    expect(result.current.state.entries).toHaveLength(2);
    expect(result.current.state.totalTokens).toBe(375); // 150 + 225

    const first = result.current.state.entries[0];
    expect(first).toMatchObject({
      agentName: "SolutionArchitect",
      inputTokens: 100,
      outputTokens: 50,
      totalTokens: 150,
    });
    // The frontend doesn't know the model, so it falls back to the agent name.
    expect(first.model).toBe("SolutionArchitect");
  });

  it("keeps a separate entry per addUsage call, even for the same agent", () => {
    const { result } = renderHook(() => useTokenTracker());
    act(() => result.current.addUsage("Tester", 10, 5));
    act(() => result.current.addUsage("Tester", 1, 1));
    expect(result.current.state.entries).toHaveLength(2);
    expect(result.current.state.totalTokens).toBe(17);
  });

  it("reset clears entries and the running total", () => {
    const { result } = renderHook(() => useTokenTracker());
    act(() => result.current.addUsage("Tester", 10, 5));
    act(() => result.current.reset());
    expect(result.current.state.entries).toEqual([]);
    expect(result.current.state.totalTokens).toBe(0);
  });
});
