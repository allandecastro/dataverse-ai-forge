"use client";

import { Link2 } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import type { SolutionTable, SolutionRelationship } from "@/hooks/useAgent";

interface ErdDiagramProps {
  tables: SolutionTable[];
  relationships: SolutionRelationship[];
}

function EntityCard({ table }: { table: SolutionTable }) {
  return (
    <div className="rounded-lg border-2 border-blue bg-bg-1 min-w-[200px] max-w-[280px]">
      {/* Header */}
      <div className="px-3 py-2 border-b border-border bg-bg-2 rounded-t-md">
        <div className="text-t-1 text-sm font-semibold">{table.displayName}</div>
        <div
          className="text-t-3 text-xs mt-0.5"
          style={{ fontFamily: "var(--font-geist-mono), monospace" }}
        >
          {table.logicalName}
        </div>
      </div>
      {/* Fields */}
      <div className="px-3 py-2">
        {table.fields.length === 0 ? (
          <span className="text-t-4 text-xs">No fields</span>
        ) : (
          <div className="space-y-1">
            {table.fields.slice(0, 8).map((field) => (
              <div
                key={field.logicalName}
                className="flex items-center justify-between gap-2"
              >
                <span className="text-t-2 text-xs truncate">
                  {field.displayName}
                </span>
                <Pill label={field.fieldType} color="blue" />
              </div>
            ))}
            {table.fields.length > 8 && (
              <span className="text-t-4 text-xs">
                +{table.fields.length - 8} more
              </span>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

function RelationshipLine({ rel }: { rel: SolutionRelationship }) {
  const typeLabel =
    rel.relationshipType === "OneToMany"
      ? "1:N"
      : rel.relationshipType === "ManyToOne"
        ? "N:1"
        : rel.relationshipType === "ManyToMany"
          ? "N:N"
          : rel.relationshipType;

  return (
    <div className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-bg-1 border border-border-lt">
      <Link2 size={12} className="text-t-4 flex-shrink-0" />
      <span
        className="text-t-2 text-xs"
        style={{ fontFamily: "var(--font-geist-mono), monospace" }}
      >
        {rel.primaryTable}
      </span>
      <Pill label={typeLabel} color="purple" />
      <span
        className="text-t-2 text-xs"
        style={{ fontFamily: "var(--font-geist-mono), monospace" }}
      >
        {rel.relatedTable}
      </span>
      <span
        className="text-t-4 text-xs ml-auto"
        style={{ fontFamily: "var(--font-geist-mono), monospace" }}
      >
        {rel.schemaName}
      </span>
    </div>
  );
}

export function ErdDiagram({ tables, relationships }: ErdDiagramProps) {
  if (tables.length === 0) {
    return (
      <div className="h-full flex items-center justify-center">
        <div className="text-t-3 text-sm text-center">
          <div className="text-t-4 mb-2">No tables created yet</div>
          <div className="text-t-4 text-xs">
            Tables will appear here as the Data Model Builder creates them
          </div>
        </div>
      </div>
    );
  }

  return (
    <div
      className="h-full overflow-auto p-6"
      style={{
        backgroundImage:
          "radial-gradient(circle, var(--border-lt) 1px, transparent 1px)",
        backgroundSize: "20px 20px",
      }}
    >
      {/* Entity cards */}
      <div className="flex flex-wrap gap-4 mb-6 justify-center">
        {tables.map((table) => (
          <EntityCard key={table.logicalName} table={table} />
        ))}
      </div>

      {/* Relationships */}
      {relationships.length > 0 && (
        <div className="max-w-2xl mx-auto">
          <h3 className="text-t-3 text-xs font-medium mb-2 uppercase tracking-wider">
            Relationships
          </h3>
          <div className="space-y-1.5">
            {relationships.map((rel) => (
              <RelationshipLine key={rel.schemaName} rel={rel} />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
