"use client";

import { useReducer, useCallback } from "react";
import type { TokenUsage, TokenTrackerState } from "@/lib/types";
import { getModelForAgent, calculateCost } from "@/lib/pricing";

type Action =
  | { type: "add"; agentName: string; inputTokens: number; outputTokens: number }
  | { type: "reset" };

function reducer(state: TokenTrackerState, action: Action): TokenTrackerState {
  switch (action.type) {
    case "add": {
      const model = getModelForAgent(action.agentName);
      const cost = calculateCost(action.agentName, action.inputTokens, action.outputTokens);
      const totalTokens = action.inputTokens + action.outputTokens;

      const entry: TokenUsage = {
        agentName: action.agentName,
        model,
        inputTokens: action.inputTokens,
        outputTokens: action.outputTokens,
        totalTokens,
        cost,
      };

      const entries = [...state.entries, entry];
      return {
        entries,
        totalTokens: state.totalTokens + totalTokens,
        totalCost: state.totalCost + cost,
      };
    }
    case "reset":
      return { entries: [], totalTokens: 0, totalCost: 0 };
  }
}

const initialState: TokenTrackerState = {
  entries: [],
  totalTokens: 0,
  totalCost: 0,
};

export function useTokenTracker() {
  const [state, dispatch] = useReducer(reducer, initialState);

  const addUsage = useCallback(
    (agentName: string, inputTokens: number, outputTokens: number) => {
      dispatch({ type: "add", agentName, inputTokens, outputTokens });
    },
    [],
  );

  const reset = useCallback(() => {
    dispatch({ type: "reset" });
  }, []);

  return { state, addUsage, reset };
}
