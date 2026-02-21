"use client";

import {
  ChevronRight,
  ChevronDown,
  Database,
  Puzzle,
  Shield,
  Table2,
} from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import type { SolutionTable } from "@/hooks/useAgent";
import type { DemoSolution } from "@/lib/demo-data";

export type SectionId = "tables" | "components" | "security";

interface SolutionTreeProps {
  collapsed: boolean;
  /** Live tables from agent state (preferred). */
  liveTables?: SolutionTable[];
  /** Fallback demo data (used when liveTables is empty). */
  solution: DemoSolution;
  expandedSections: Set<SectionId>;
  onToggleSection: (id: SectionId) => void;
  expandedTables: Set<string>;
  onToggleTable: (logicalName: string) => void;
  /** Navigate to table detail view when a table name is clicked. */
  onSelectTable?: (logicalName: string) => void;
}

const sectionMeta: { id: SectionId; label: string; icon: typeof Database }[] = [
  { id: "tables", label: "TABLES", icon: Database },
  { id: "components", label: "COMPONENTS", icon: Puzzle },
  { id: "security", label: "SECURITY", icon: Shield },
];

function fieldPillColor(fieldType: string): "blue" | "neutral" {
  return fieldType === "Lookup" ? "blue" : "neutral";
}

export function SolutionTree({
  collapsed,
  liveTables,
  solution,
  expandedSections,
  onToggleSection,
  expandedTables,
  onToggleTable,
  onSelectTable,
}: SolutionTreeProps) {
  if (collapsed) return null;

  // Use live tables if available, otherwise fall back to demo data
  const hasLiveData = liveTables && liveTables.length > 0;
  const tables = hasLiveData
    ? liveTables.map((t) => ({
        logicalName: t.logicalName,
        displayName: t.displayName,
        fields: t.fields.map((f) => ({
          logicalName: f.logicalName,
          displayName: f.displayName,
          fieldType: f.fieldType,
        })),
      }))
    : solution.tables;

  return (
    <div className="flex-1 overflow-y-auto min-h-0 px-3 py-1">
      {sectionMeta.map(({ id, label, icon: SectionIcon }) => {
        const isExpanded = expandedSections.has(id);
        const count =
          id === "tables"
            ? tables.length
            : id === "components"
              ? solution.components.length
              : solution.securityRoles.length;

        return (
          <div key={id} className="mb-1">
            {/* Section header */}
            <button
              onClick={() => onToggleSection(id)}
              className="flex w-full items-center gap-1.5 rounded-md px-1 py-1 text-t-3 hover:text-t-2 hover:bg-bg-2 transition-colors"
            >
              {isExpanded ? (
                <ChevronDown size={12} className="flex-shrink-0" />
              ) : (
                <ChevronRight size={12} className="flex-shrink-0" />
              )}
              <SectionIcon size={12} className="flex-shrink-0" />
              <span
                className="flex-1 text-left font-medium"
                style={{ fontSize: 11, letterSpacing: "0.05em" }}
              >
                {label}
              </span>
              <Pill label={String(count)} color="neutral" />
            </button>

            {/* Section content */}
            {isExpanded && (
              <div className="ml-2 mt-0.5">
                {id === "tables" &&
                  tables.map((table) => {
                    const tableExpanded = expandedTables.has(table.logicalName);
                    return (
                      <div key={table.logicalName}>
                        <div className="flex w-full items-center gap-1.5 rounded-md px-1.5 py-1 text-xs text-t-2 hover:bg-bg-2 transition-colors">
                          <button
                            onClick={() => onToggleTable(table.logicalName)}
                            className="flex-shrink-0"
                          >
                            {tableExpanded ? (
                              <ChevronDown size={11} className="text-t-4" />
                            ) : (
                              <ChevronRight size={11} className="text-t-4" />
                            )}
                          </button>
                          <button
                            onClick={() => onSelectTable?.(table.logicalName)}
                            className="flex items-center gap-1.5 flex-1 min-w-0 text-left"
                          >
                            <Table2 size={12} className="text-t-3 flex-shrink-0" />
                            <span className="flex-1 truncate">
                              {table.displayName}
                            </span>
                          </button>
                          <span className="text-t-4 flex-shrink-0" style={{ fontSize: 10 }}>
                            {table.fields.length}
                          </span>
                        </div>

                        {tableExpanded && (
                          <div className="ml-4 border-l border-border-lt pl-2 mt-0.5 mb-1">
                            {table.fields.map((field) => (
                              <div
                                key={field.logicalName}
                                className="flex items-center gap-1.5 py-0.5 text-xs text-t-3"
                              >
                                <span className="flex-1 truncate">
                                  {field.displayName}
                                </span>
                                <Pill
                                  label={field.fieldType}
                                  color={fieldPillColor(field.fieldType)}
                                />
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    );
                  })}

                {id === "components" &&
                  solution.components.map((comp) => (
                    <div
                      key={comp.name}
                      className="flex items-center gap-1.5 rounded-md px-1.5 py-1 text-xs text-t-2"
                    >
                      <Puzzle size={12} className="text-t-4 flex-shrink-0" />
                      <span className="flex-1 truncate">{comp.name}</span>
                      <span className="text-t-4" style={{ fontSize: 10 }}>
                        {comp.type}
                      </span>
                    </div>
                  ))}

                {id === "security" &&
                  solution.securityRoles.map((role) => (
                    <div
                      key={role.name}
                      className="flex items-center gap-1.5 rounded-md px-1.5 py-1 text-xs text-t-2"
                    >
                      <Shield size={12} className="text-t-4 flex-shrink-0" />
                      <span className="truncate">{role.name}</span>
                    </div>
                  ))}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
