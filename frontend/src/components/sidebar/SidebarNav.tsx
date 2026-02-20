"use client";

import { LayoutDashboard, Activity, Network, type LucideIcon } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";

export type NavItemId = "overview" | "activity" | "erd";

interface NavItem {
  id: NavItemId;
  label: string;
  icon: LucideIcon;
}

const navItems: NavItem[] = [
  { id: "overview", label: "Overview", icon: LayoutDashboard },
  { id: "activity", label: "Activity Log", icon: Activity },
  { id: "erd", label: "ERD Diagram", icon: Network },
];

interface SidebarNavProps {
  collapsed: boolean;
  activeItem: NavItemId;
  onItemClick: (id: NavItemId) => void;
  activityCount: number;
}

export function SidebarNav({
  collapsed,
  activeItem,
  onItemClick,
  activityCount,
}: SidebarNavProps) {
  return (
    <nav className="flex flex-col gap-0.5 px-2 py-1">
      {navItems.map((item) => {
        const Icon = item.icon;
        const isActive = activeItem === item.id;

        return (
          <button
            key={item.id}
            onClick={() => onItemClick(item.id)}
            title={collapsed ? item.label : undefined}
            className={`flex items-center gap-2.5 rounded-md px-2.5 py-1.5 text-xs transition-colors ${
              isActive
                ? "bg-bg-3 text-t-1"
                : "text-t-3 hover:bg-bg-2 hover:text-t-2"
            }`}
            style={collapsed ? { justifyContent: "center" } : undefined}
          >
            <Icon size={14} className="flex-shrink-0" />
            {!collapsed && (
              <>
                <span className="flex-1 text-left">{item.label}</span>
                {item.id === "activity" && activityCount > 0 && (
                  <Pill label={String(activityCount)} color="neutral" />
                )}
              </>
            )}
          </button>
        );
      })}
    </nav>
  );
}
