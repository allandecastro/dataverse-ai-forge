"use client";

import { Package } from "lucide-react";

interface SolutionBadgeProps {
  collapsed: boolean;
  displayName: string;
  prefix: string;
  version: string;
}

export function SolutionBadge({
  collapsed,
  displayName,
  prefix,
  version,
}: SolutionBadgeProps) {
  if (collapsed) {
    return (
      <div className="flex justify-center px-3 py-2">
        <Package size={14} className="text-t-3" />
      </div>
    );
  }

  return (
    <div className="mx-3 my-2 rounded-md border border-border bg-bg-2 px-2.5 py-2">
      <div className="flex items-center gap-2">
        <Package size={14} className="text-t-2 flex-shrink-0" />
        <span className="text-t-1 text-xs font-medium truncate">
          {displayName}
        </span>
      </div>
      <span
        className="text-t-3 mt-0.5 block truncate font-mono"
        style={{ fontSize: 11 }}
      >
        {prefix}_ · v{version}
      </span>
    </div>
  );
}
