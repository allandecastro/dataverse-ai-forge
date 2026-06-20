"use client";

import type { ButtonHTMLAttributes, Ref } from "react";
import { cn } from "@/lib/cn";

type ButtonVariant = "primary" | "secondary" | "danger" | "ghostDanger";
type ButtonSize = "sm" | "md";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  // React 19 passes ref as a plain prop — accept it so callers that manage
  // focus (e.g. ConfirmDialog focusing Cancel) can attach one.
  ref?: Ref<HTMLButtonElement>;
}

// Single source of truth for action-button styling so Cancel / Save / Test /
// Sign-out (and any other action pair) share one look instead of each call site
// hand-rolling its own padding + border + colour. Flat by design — radius comes
// from the global system (no rounded utility here).
const variantClass: Record<ButtonVariant, string> = {
  primary: "bg-accent text-accent-fg hover:opacity-90",
  secondary: "border border-border bg-bg-2 text-t-2 hover:bg-bg-3 hover:text-t-1",
  danger: "bg-red text-white hover:opacity-90",
  // Low-emphasis destructive action (no fill/border) — for tertiary actions like
  // Reject / Dismiss that shouldn't compete visually with a filled primary CTA.
  // Self-contained colour (cn() has no tailwind-merge, so callers can't safely
  // override a variant's text colour — bake it in here instead).
  ghostDanger: "text-red hover:bg-bg-2",
};

const sizeClass: Record<ButtonSize, string> = {
  sm: "px-3 py-1 text-xs",
  md: "px-4 py-1.5 text-xs",
};

export function Button({
  variant = "secondary",
  size = "md",
  className,
  children,
  type = "button",
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={cn(
        "inline-flex items-center justify-center gap-1.5 font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed",
        variantClass[variant],
        sizeClass[size],
        className,
      )}
      {...rest}
    >
      {children}
    </button>
  );
}
