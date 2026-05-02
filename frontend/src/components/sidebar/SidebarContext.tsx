"use client";

import { createContext, useContext, type ReactNode } from "react";

export type PanelId = "timeline" | "activity";

/**
 * State + actions consumed by Sidebar and its children. Centralizing here turned
 * a 23-prop component into one that takes no props and reads what it needs from
 * context — keeping page.tsx and DemoWorkspaceContent.tsx as the only places that
 * have to know about the data shape.
 */
export interface SidebarContextValue {
  collapsed: boolean;
  onCollapsedChange: (collapsed: boolean) => void;
  tokenCount: number;
  environmentName?: string;
  environmentUrl?: string;
  dataverseConnected?: boolean;
  solutionName?: string;
  solutionPrefix?: string;
  solutionVersion?: string;
  onTokenPillClick?: () => void;
  projectName?: string;
  onSwitchProject?: () => void;
  onRenameProject?: (newName: string) => Promise<boolean> | boolean;
  onSettingsClick?: () => void;
  activePanel: PanelId | null;
  onTogglePanel: (id: PanelId) => void;
  activityCount?: number;
  diagOpen?: boolean;
  onToggleDiag?: () => void;
  hasNewToolCalls?: boolean;
}

const SidebarContext = createContext<SidebarContextValue | null>(null);

export function SidebarProvider({
  value,
  children,
}: {
  value: SidebarContextValue;
  children: ReactNode;
}) {
  return <SidebarContext.Provider value={value}>{children}</SidebarContext.Provider>;
}

export function useSidebar(): SidebarContextValue {
  const ctx = useContext(SidebarContext);
  if (!ctx) {
    throw new Error("useSidebar must be used inside a <SidebarProvider>");
  }
  return ctx;
}
