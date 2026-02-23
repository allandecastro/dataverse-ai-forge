"use client";

import { Brain } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { AppSettings } from "@/hooks/useSettings";

interface AzureOpenAiSettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
  status?: "connected" | "unavailable" | "unknown";
  testing: boolean;
  onTest: () => void;
}

export function AzureOpenAiSettings({
  settings,
  onChange,
  status,
  testing,
  onTest,
}: AzureOpenAiSettingsProps) {
  return (
    <SettingsSection
      icon={Brain}
      title="Azure OpenAI"
      status={status}
      requiresRestart
      testable
      testing={testing}
      onTest={onTest}
    >
      <SettingsInput
        label="Endpoint"
        value={settings.azureOpenAiEndpoint}
        onChange={(v) => onChange({ azureOpenAiEndpoint: v })}
        placeholder="https://your-resource.openai.azure.com"
        monospace
      />
      <SettingsInput
        label="API Key"
        value={settings.azureOpenAiApiKey}
        onChange={(v) => onChange({ azureOpenAiApiKey: v })}
        placeholder="Enter API key"
        type="password"
        monospace
      />
      <SettingsInput
        label="GPT-4o Deployment Name"
        value={settings.azureOpenAiGpt4oDeploymentName}
        onChange={(v) => onChange({ azureOpenAiGpt4oDeploymentName: v })}
        placeholder="gpt-4o"
        monospace
      />
      <SettingsInput
        label="GPT-4o Mini Deployment Name"
        value={settings.azureOpenAiGpt4oMiniDeploymentName}
        onChange={(v) => onChange({ azureOpenAiGpt4oMiniDeploymentName: v })}
        placeholder="gpt-4o-mini"
        monospace
      />
    </SettingsSection>
  );
}
