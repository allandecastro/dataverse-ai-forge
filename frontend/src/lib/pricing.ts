interface ModelRate {
  inputPer1M: number;
  outputPer1M: number;
}

const modelPricing: Record<string, ModelRate> = {
  "gpt-4o": { inputPer1M: 2.5, outputPer1M: 10.0 },
  "gpt-4o-mini": { inputPer1M: 0.15, outputPer1M: 0.6 },
};

const agentModel: Record<string, string> = {
  SolutionArchitect: "gpt-4o",
  BusinessAnalyst: "gpt-4o",
  Reviewer: "gpt-4o-mini",
  DataModelBuilder: "gpt-4o-mini",
  UIBuilder: "gpt-4o-mini",
  AutomationBuilder: "gpt-4o",
  SecurityBuilder: "gpt-4o-mini",
  DataPopulation: "gpt-4o-mini",
  Tester: "gpt-4o-mini",
  Documentor: "gpt-4o-mini",
};

export function getModelForAgent(agentName: string): string {
  return agentModel[agentName] ?? "gpt-4o-mini";
}

export function calculateCost(
  agentName: string,
  inputTokens: number,
  outputTokens: number,
): number {
  const model = getModelForAgent(agentName);
  const rate = modelPricing[model] ?? modelPricing["gpt-4o-mini"];
  return (
    (inputTokens / 1_000_000) * rate.inputPer1M +
    (outputTokens / 1_000_000) * rate.outputPer1M
  );
}
