"use client";

import { useState, useEffect, useCallback } from "react";
import { SidebarHeader } from "./SidebarHeader";
import { SidebarSearch } from "./SidebarSearch";
import { SidebarNav, type NavItemId } from "./SidebarNav";
import { SolutionBadge } from "./SolutionBadge";
import { SolutionTree, type SectionId } from "./SolutionTree";
import { SidebarFooter } from "./SidebarFooter";
import { demoSolution, demoEnvironment } from "@/lib/demo-data";
import type { SolutionTable } from "@/hooks/useAgent";

interface SidebarProps {
  collapsed: boolean;
  onCollapsedChange: (collapsed: boolean) => void;
  tokenCount: number;
  cost: number;
  liveTables?: SolutionTable[];
  solutionName?: string;
  solutionPrefix?: string;
  solutionVersion?: string;
}

function getStoredSet<T extends string>(key: string, defaults: T[]): Set<T> {
  if (typeof window === "undefined") return new Set(defaults);
  try {
    const stored = localStorage.getItem(key);
    if (stored) return new Set(JSON.parse(stored) as T[]);
  } catch {
    // ignore
  }
  return new Set(defaults);
}

function persistSet(key: string, set: Set<string>) {
  localStorage.setItem(key, JSON.stringify([...set]));
}

export function Sidebar({ collapsed, onCollapsedChange, tokenCount, cost, liveTables, solutionName, solutionPrefix, solutionVersion }: SidebarProps) {
  const [activeNav, setActiveNav] = useState<NavItemId>("overview");
  const [expandedSections, setExpandedSections] = useState<Set<SectionId>>(
    () => getStoredSet<SectionId>("sidebar-sections", ["tables"])
  );
  const [expandedTables, setExpandedTables] = useState<Set<string>>(
    () => getStoredSet<string>("sidebar-tables", [])
  );

  useEffect(() => {
    persistSet("sidebar-sections", expandedSections);
  }, [expandedSections]);

  useEffect(() => {
    persistSet("sidebar-tables", expandedTables);
  }, [expandedTables]);

  const toggleSection = useCallback((id: SectionId) => {
    setExpandedSections((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const toggleTable = useCallback((logicalName: string) => {
    setExpandedTables((prev) => {
      const next = new Set(prev);
      if (next.has(logicalName)) next.delete(logicalName);
      else next.add(logicalName);
      return next;
    });
  }, []);

  return (
    <aside
      className="flex-shrink-0 flex flex-col h-full border-r border-border bg-bg-1 transition-all duration-300 overflow-hidden"
      style={{ width: collapsed ? 52 : 252 }}
    >
      <SidebarHeader collapsed={collapsed} />
      <SidebarSearch collapsed={collapsed} />
      <SidebarNav
        collapsed={collapsed}
        activeItem={activeNav}
        onItemClick={setActiveNav}
        activityCount={12}
      />

      <div className="border-t border-border-lt" />

      <SolutionBadge
        collapsed={collapsed}
        displayName={solutionName || demoSolution.displayName}
        prefix={solutionPrefix || demoSolution.prefix}
        version={solutionVersion || demoSolution.version}
      />

      <SolutionTree
        collapsed={collapsed}
        liveTables={liveTables}
        solution={demoSolution}
        expandedSections={expandedSections}
        onToggleSection={toggleSection}
        expandedTables={expandedTables}
        onToggleTable={toggleTable}
      />

      <SidebarFooter
        collapsed={collapsed}
        environment={demoEnvironment}
        tokenCount={tokenCount}
        cost={cost}
        onToggleCollapse={() => onCollapsedChange(!collapsed)}
      />
    </aside>
  );
}
