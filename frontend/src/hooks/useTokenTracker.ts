"use client";

import { useReducer, useCallback } from "react";
import type { TokenUsage, TokenTrackerState } from "@/lib/types";

type Action =
  | { type: "add"; agentName: string; model: string; inputTokens: number; outputTokens: number }
  | { type: "reset" };

function reducer(state: TokenTrackerState, action: Action): TokenTrackerState {
  switch (action.type) {
    case "add": {
      const totalTokens = action.inputTokens + action.outputTokens;
      const entry: TokenUsage = {
        agentName: action.agentName,
        model: action.model,
        inputTokens: action.inputTokens,
        outputTokens: action.outputTokens,
        totalTokens,
      };
      return {
        entries: [...state.entries, entry],
        totalTokens: state.totalTokens + totalTokens,
      };
    }
    case "reset":
      return { entries: [], totalTokens: 0 };
  }
}

const initialState: TokenTrackerState = {
  entries: [],
  totalTokens: 0,
};

export function useTokenTracker() {
  const [state, dispatch] = useReducer(reducer, initialState);

  const addUsage = useCallback(
    (agentName: string, inputTokens: number, outputTokens: number) => {
      // Model is unknown to the frontend (the backend picks per agent and we don't surface it).
      // Display layer falls back to the agent name when needed.
      dispatch({ type: "add", agentName, model: agentName, inputTokens, outputTokens });
    },
    [],
  );

  const reset = useCallback(() => {
    dispatch({ type: "reset" });
  }, []);

  return { state, addUsage, reset };
}
