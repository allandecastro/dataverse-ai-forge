"use client";

import { GitBranch } from "lucide-react";
import { SettingsSection } from "./SettingsSection";
import { SettingsInput } from "./SettingsInput";
import type { ProjectConnection } from "@/hooks/useProjectConnection";

interface AzureDevOpsSettingsProps {
  settings: ProjectConnection;
  onChange: (patch: Partial<ProjectConnection>) => void;
}

/**
 * Optional: the project's Azure DevOps backlog, which the Documentary Agent can ingest
 * into the project's knowledge base. Per project, like the Dataverse connection.
 */
export function AzureDevOpsSettings({ settings, onChange }: AzureDevOpsSettingsProps) {
  return (
    <SettingsSection icon={GitBranch} title="Azure DevOps (optional)">
      <p className="text-t-4 text-xs leading-relaxed">
        Only needed to ingest this project&apos;s backlog into its knowledge base.
      </p>
      <SettingsInput
        label="Organization URL"
        value={settings.azureDevOpsOrganizationUrl}
        onChange={(v) => onChange({ azureDevOpsOrganizationUrl: v })}
        placeholder="https://dev.azure.com/your-org"
        monospace
      />
      <SettingsInput
        label="Project"
        value={settings.azureDevOpsProject}
        onChange={(v) => onChange({ azureDevOpsProject: v })}
        placeholder="Project name"
      />
      <SettingsInput
        label="Personal access token"
        value={settings.azureDevOpsPersonalAccessToken}
        onChange={(v) => onChange({ azureDevOpsPersonalAccessToken: v })}
        placeholder="PAT with Work Items (Read)"
        type="password"
        monospace
      />
    </SettingsSection>
  );
}
