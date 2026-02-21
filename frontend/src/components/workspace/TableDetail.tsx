"use client";

import { CheckCircle2, Link2 } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";
import type { SolutionTable, SolutionRelationship } from "@/hooks/useAgent";

interface TableDetailProps {
  table: SolutionTable;
  relationships: SolutionRelationship[];
}

function fieldTypePill(
  fieldType: string
): "blue" | "green" | "purple" | "neutral" {
  if (fieldType === "Lookup") return "blue";
  if (fieldType === "StringType" || fieldType === "Text") return "green";
  if (fieldType === "Choice" || fieldType === "OptionSet") return "purple";
  return "neutral";
}

export function TableDetail({ table, relationships }: TableDetailProps) {
  // Filter relationships relevant to this table
  const tableRelationships = relationships.filter(
    (r) =>
      r.primaryTable === table.logicalName ||
      r.relatedTable === table.logicalName
  );

  return (
    <div className="h-full overflow-y-auto p-6">
      {/* Header */}
      <div className="mb-6">
        <div className="flex items-center gap-3">
          <h1 className="text-t-1 text-xl font-semibold">
            {table.displayName}
          </h1>
          <Pill label="Created" color="green" />
        </div>
        <span
          className="text-t-3 text-xs mt-1 block"
          style={{ fontFamily: "var(--font-geist-mono), monospace" }}
        >
          {table.logicalName}
        </span>

        {/* Metadata pills */}
        <div className="flex gap-2 mt-3">
          <Pill
            label={`${table.fields.length} fields`}
            color="neutral"
          />
          <Pill
            label={`${tableRelationships.length} relationships`}
            color="neutral"
          />
        </div>
      </div>

      {/* Fields table */}
      <div className="mb-6">
        <h2 className="text-t-2 text-sm font-medium mb-3">Fields</h2>
        {table.fields.length === 0 ? (
          <div className="text-t-4 text-xs py-4 text-center">
            No fields defined
          </div>
        ) : (
          <div className="rounded-lg border border-border overflow-hidden">
            <table className="w-full text-xs">
              <thead>
                <tr className="bg-bg-2 text-t-3">
                  <th className="text-left px-3 py-2 font-medium">Field</th>
                  <th className="text-left px-3 py-2 font-medium">
                    Logical Name
                  </th>
                  <th className="text-left px-3 py-2 font-medium">Type</th>
                  <th className="text-center px-3 py-2 font-medium">Status</th>
                </tr>
              </thead>
              <tbody>
                {table.fields.map((field, i) => (
                  <tr
                    key={field.logicalName}
                    className={`${i % 2 === 0 ? "bg-bg-1" : "bg-bg-0"} hover:bg-bg-2 transition-colors`}
                  >
                    <td className="px-3 py-2 text-t-1">{field.displayName}</td>
                    <td
                      className="px-3 py-2 text-t-3"
                      style={{
                        fontFamily: "var(--font-geist-mono), monospace",
                      }}
                    >
                      {field.logicalName}
                    </td>
                    <td className="px-3 py-2">
                      <Pill
                        label={field.fieldType}
                        color={fieldTypePill(field.fieldType)}
                      />
                    </td>
                    <td className="px-3 py-2 text-center">
                      <CheckCircle2
                        size={14}
                        className="text-green mx-auto"
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Relationships */}
      {tableRelationships.length > 0 && (
        <div>
          <h2 className="text-t-2 text-sm font-medium mb-3">Relationships</h2>
          <div className="grid grid-cols-1 gap-3">
            {tableRelationships.map((rel) => {
              const isParent = rel.primaryTable === table.logicalName;
              const otherTable = isParent
                ? rel.relatedTable
                : rel.primaryTable;
              const typeLabel =
                rel.relationshipType === "OneToMany"
                  ? isParent
                    ? "1:N"
                    : "N:1"
                  : rel.relationshipType;

              return (
                <div
                  key={rel.schemaName}
                  className="rounded-lg border border-border bg-bg-1 p-3"
                >
                  <div className="flex items-center gap-2 mb-1">
                    <Link2 size={14} className="text-t-3" />
                    <span className="text-t-1 text-sm font-medium">
                      {otherTable}
                    </span>
                    <Pill label={typeLabel} color="purple" />
                  </div>
                  <span
                    className="text-t-3 text-xs"
                    style={{
                      fontFamily: "var(--font-geist-mono), monospace",
                    }}
                  >
                    {rel.schemaName}
                  </span>
                </div>
              );
            })}
          </div>
        </div>
      )}
    </div>
  );
}
