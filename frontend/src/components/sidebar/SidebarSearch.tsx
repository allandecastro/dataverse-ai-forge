"use client";

import { Search } from "lucide-react";

interface SidebarSearchProps {
  collapsed: boolean;
}

export function SidebarSearch({ collapsed }: SidebarSearchProps) {
  if (collapsed) {
    return (
      <div className="flex justify-center px-3 py-2">
        <Search size={14} className="text-t-3" />
      </div>
    );
  }

  return (
    <div className="px-3 py-2">
      <div className="flex items-center gap-2 rounded-md border border-border bg-bg-2 px-2.5 py-1.5">
        <Search size={13} className="text-t-4 flex-shrink-0" />
        <span className="text-t-4 text-xs flex-1">Search...</span>
        <kbd
          className="rounded border border-border bg-bg-1 px-1.5 py-0.5 text-t-4"
          style={{ fontSize: 10, fontFamily: "var(--font-geist-mono), monospace" }}
        >
          ⌘K
        </kbd>
      </div>
    </div>
  );
}
