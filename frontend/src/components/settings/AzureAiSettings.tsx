"use client";

import { useState } from "react";
import { Brain, ChevronRight } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import { agents } from "@/lib/agents";
import type { AppSettings } from "@/hooks/useSettings";

interface AzureAiSettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
  status?: "connected" | "unavailable" | "unknown";
  testing: boolean;
  onTest: () => void;
}

// The eight active agents, in pipeline order. Documentor is intentionally left
// out (roadmap only — not in the backend's AgentNames). Keys MUST match the
// backend agent names so AgentModels resolves server-side.
const AGENT_ORDER = [
  "SolutionArchitect",
  "BusinessAnalyst",
  "DataModelBuilder",
  "UIBuilder",
  "DataPopulation",
  "SecurityBuilder",
  "AutomationBuilder",
  "Tester",
] as const;

export function AzureAiSettings({
  settings,
  onChange,
  status,
  testing,
  onTest,
}: AzureAiSettingsProps) {
  const [showPerAgent, setShowPerAgent] = useState(false);

  const models = settings.azureAiAgentModels ?? {};
  const globalModel = settings.azureAiDefaultModel?.trim() || "gpt-4o";
  const overrideCount = AGENT_ORDER.filter((a) => (models[a] ?? "").trim()).length;

  function setAgentModel(agent: string, value: string) {
    const next = { ...models };
    if (value.trim()) next[agent] = value;
    else delete next[agent];
    onChange({ azureAiAgentModels: next });
  }

  return (
    <SettingsSection
      icon={Brain}
      title="Azure AI (Foundry)"
      status={status}
      testable
      testing={testing}
      onTest={onTest}
    >
      <SettingsInput
        label="Endpoint"
        value={settings.azureAiEndpoint}
        onChange={(v) => onChange({ azureAiEndpoint: v })}
        placeholder="https://your-project.services.ai.azure.com"
        monospace
      />
      <SettingsInput
        label="API Key"
        value={settings.azureAiApiKey}
        onChange={(v) => onChange({ azureAiApiKey: v })}
        placeholder="Enter API key"
        type="password"
        monospace
      />
      <SettingsInput
        label="Default Model"
        value={settings.azureAiDefaultModel}
        onChange={(v) => onChange({ azureAiDefaultModel: v })}
        placeholder="gpt-4o"
        monospace
      />

      {/* Optional per-agent model overrides. Same endpoint + key; only the model
          name in the request body changes. Empty field = use the default model. */}
      <div className="mt-1">
        <button
          type="button"
          onClick={() => setShowPerAgent((s) => !s)}
          className="flex items-center gap-1.5 text-t-2 text-xs hover:text-t-1 transition-colors"
        >
          <ChevronRight
            size={13}
            className={`transition-transform ${showPerAgent ? "rotate-90" : ""}`}
          />
          Per-agent model
          <span className="text-t-4">(optional)</span>
          {overrideCount > 0 && (
            <span className="text-accent-ink">
              {overrideCount} override{overrideCount !== 1 ? "s" : ""}
            </span>
          )}
        </button>

        {showPerAgent && (
          <div className="mt-2 flex flex-col gap-2">
            <p className="text-t-4 text-xs leading-relaxed">
              Leave a field empty to use the default model. Each model must be
              deployed on the same Foundry resource and support tool calling.
            </p>
            {AGENT_ORDER.map((agentName) => (
              <SettingsInput
                key={agentName}
                label={agents[agentName]?.displayName ?? agentName}
                value={models[agentName] ?? ""}
                onChange={(v) => setAgentModel(agentName, v)}
                placeholder={globalModel}
                monospace
              />
            ))}
          </div>
        )}
      </div>
    </SettingsSection>
  );
}
