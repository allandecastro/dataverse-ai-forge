"use client";

import { Github } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { AppSettings } from "@/hooks/useSettings";

interface GitHubSettingsProps {
  settings: AppSettings;
  onChange: (patch: Partial<AppSettings>) => void;
  status?: "connected" | "unavailable" | "unknown";
  testing: boolean;
  onTest: () => void;
}

export function GitHubSettings({
  settings,
  onChange,
  status,
  testing,
  onTest,
}: GitHubSettingsProps) {
  return (
    <SettingsSection
      icon={Github}
      title="GitHub"
      status={status}
      requiresRestart
      testable
      testing={testing}
      onTest={onTest}
    >
      <SettingsInput
        label="Personal Access Token"
        value={settings.gitHubToken}
        onChange={(v) => onChange({ gitHubToken: v })}
        placeholder="ghp_xxxxxxxxxxxx"
        type="password"
        monospace
        description="Token with repo scope for pushing documentation and plugin code."
      />
    </SettingsSection>
  );
}
