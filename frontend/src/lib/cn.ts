/**
 * Tiny conditional-className joiner. Accepts strings, falsy values, and arrays;
 * drops anything falsy and joins the rest with a single space. Keeps the
 * codebase from hand-concatenating template literals (and dropping spaces).
 *
 * Dependency-free on purpose — we don't need tailwind-merge's conflict
 * resolution for the current usage; if that becomes necessary, swap the impl
 * here and every call site benefits.
 */
export type ClassValue = string | number | false | null | undefined | ClassValue[];

export function cn(...values: ClassValue[]): string {
  const out: string[] = [];
  for (const v of values) {
    if (!v) continue;
    if (Array.isArray(v)) {
      const nested = cn(...v);
      if (nested) out.push(nested);
    } else {
      out.push(String(v));
    }
  }
  return out.join(" ");
}
