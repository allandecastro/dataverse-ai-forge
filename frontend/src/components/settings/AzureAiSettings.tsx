"use client";

import { Brain } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { AppSettings } from "@/hooks/useSettings";

interface AzureAiSettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
  status?: "connected" | "unavailable" | "unknown";
  testing: boolean;
  onTest: () => void;
}

export function AzureAiSettings({
  settings,
  onChange,
  status,
  testing,
  onTest,
}: AzureAiSettingsProps) {
  return (
    <SettingsSection
      icon={Brain}
      title="Azure AI (Foundry)"
      status={status}
      requiresRestart
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
    </SettingsSection>
  );
}
