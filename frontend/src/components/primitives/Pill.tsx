"use client";

interface PillProps {
  label: string;
  color?: "green" | "orange" | "red" | "blue" | "purple" | "neutral";
}

const colorMap = {
  green: "bg-green-bg text-green",
  orange: "bg-orange-bg text-orange",
  red: "bg-red-bg text-red",
  blue: "bg-blue-bg text-blue",
  purple: "bg-purple-bg text-purple",
  neutral: "bg-bg-3 text-t-3",
};

export function Pill({ label, color = "neutral" }: PillProps) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 font-medium ${colorMap[color]}`}
      style={{ fontSize: 11 }}
    >
      {label}
    </span>
  );
}
