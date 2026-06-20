"use client";

import type { LucideIcon } from "lucide-react";
import type { ReactNode } from "react";
import { Loader2, Info } from "lucide-react";
import { ConnectionStatus } from "./ConnectionStatus";
import { Button } from "@/components/primitives/Button";

interface SettingsSectionProps {
  icon: LucideIcon;
  title: string;
  status?: "connected" | "unavailable" | "unknown";
  requiresRestart?: boolean;
  testable?: boolean;
  testing?: boolean;
  onTest?: () => void;
  children: ReactNode;
}

export function SettingsSection({
  icon: Icon,
  title,
  status,
  requiresRestart,
  testable,
  testing,
  onTest,
  children,
}: SettingsSectionProps) {
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Icon size={16} className="text-t-2" />
          <h3 className="text-t-1 text-sm font-semibold">{title}</h3>
          {status && <ConnectionStatus status={status} />}
        </div>
        {testable && (
          <Button variant="secondary" size="sm" onClick={onTest} disabled={testing}>
            {testing ? (
              <>
                <Loader2 size={12} className="animate-spin" />
                Testing...
              </>
            ) : (
              "Test Connection"
            )}
          </Button>
        )}
      </div>

      {requiresRestart && (
        <div className="flex items-center gap-1.5 rounded bg-bg-2 px-3 py-1.5 text-xs text-t-3">
          <Info size={12} className="text-orange flex-shrink-0" />
          Changes to this section require a backend restart to take effect.
        </div>
      )}

      <div className="space-y-3">
        {children}
      </div>
    </div>
  );
}
