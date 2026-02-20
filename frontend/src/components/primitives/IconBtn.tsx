"use client";

import type { LucideIcon } from "lucide-react";

interface IconBtnProps {
  icon: LucideIcon;
  onClick?: () => void;
  active?: boolean;
  title?: string;
}

export function IconBtn({ icon: Icon, onClick, active, title }: IconBtnProps) {
  return (
    <button
      onClick={onClick}
      title={title}
      className={`flex items-center justify-center rounded border transition-colors ${
        active
          ? "border-border-hi bg-bg-3 text-t-1"
          : "border-transparent text-t-3 hover:border-border hover:bg-bg-2 hover:text-t-2"
      }`}
      style={{ width: 28, height: 28 }}
    >
      <Icon size={14} />
    </button>
  );
}
