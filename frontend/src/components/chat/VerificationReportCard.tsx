"use client";

import { CheckCircle2, AlertTriangle, XCircle } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";

/**
 * Tester emits a structured "VERIFICATION REPORT" block. Rendered as a polished
 * card with a colored verdict pill, per-bullet status icons, and an optional
 * trailing note — instead of the raw markdown the agent prompt produces.
 *
 * Detection: any assistant message whose first non-blank line starts with
 * "VERIFICATION REPORT" (case-insensitive). Defensive — the Tester sometimes
 * adds trailing prose ("Handing off to Solution Architect…"); we keep that as
 * a footnote outside the card.
 */

export interface ParsedVerificationReport {
  verdict: "PASS" | "FAIL" | "PARTIAL";
  bullets: { text: string; isFailure: boolean }[];
  trailing: string;
}

const REPORT_PREFIX_RE = /^\s*VERIFICATION REPORT\b/i;

export function isVerificationReport(content: string): boolean {
  if (!content) return false;
  const firstLine = content.trimStart().split(/\r?\n/, 1)[0];
  return REPORT_PREFIX_RE.test(firstLine);
}

/**
 * Parse the report into a structured shape. We accept loose formatting
 * (extra blanks, missing colons) — Tester's emit shape is deterministic but
 * we don't want a stray space to break rendering.
 */
export function parseVerificationReport(content: string): ParsedVerificationReport | null {
  const lines = content.replace(/\r\n/g, "\n").split("\n");

  let verdict: ParsedVerificationReport["verdict"] = "PASS";
  let verdictFound = false;
  const bullets: ParsedVerificationReport["bullets"] = [];
  const trailing: string[] = [];
  let inTrailing = false;

  for (const raw of lines) {
    const line = raw.trim();
    if (!line) {
      // A blank line after we've started bullets opens the trailing block.
      if (bullets.length > 0) inTrailing = true;
      continue;
    }
    if (REPORT_PREFIX_RE.test(line)) continue;

    if (!verdictFound && /^verdict\b/i.test(line)) {
      const m = line.match(/(PASS|FAIL|PARTIAL)/i);
      if (m) {
        verdict = m[1].toUpperCase() as ParsedVerificationReport["verdict"];
        verdictFound = true;
      }
      continue;
    }

    if (line.startsWith("-") || line.startsWith("•")) {
      const text = line.replace(/^[-•]\s*/, "").trim();
      // Heuristic: a bullet is a failure if the verdict is FAIL/PARTIAL and the
      // text doesn't say "matches plan" / "PASS" or contains words like
      // "expected", "missing", "not found", "got", or starts with "🔴" / "❌".
      const lower = text.toLowerCase();
      const looksFail = verdict !== "PASS" && (
        /\b(expected|missing|not found|got |fail|mismatch|unexpected|invalid)\b/i.test(text)
        || lower.startsWith("🔴")
        || lower.startsWith("❌")
      );
      bullets.push({ text, isFailure: looksFail });
      continue;
    }

    // Anything else after we've seen at least one bullet goes to the trailing block.
    if (bullets.length > 0 || inTrailing) {
      trailing.push(line);
    }
  }

  if (bullets.length === 0 && !verdictFound) return null;

  return {
    verdict,
    bullets,
    trailing: trailing.join("\n").trim(),
  };
}

interface VerificationReportCardProps {
  content: string;
  agentColor?: string;
}

export function VerificationReportCard({ content, agentColor }: VerificationReportCardProps) {
  const parsed = parseVerificationReport(content);
  if (!parsed) {
    // Defensive: if parsing fails (model went off-script), fall back to a
    // plain-text bubble. The caller decides whether to render this at all.
    return (
      <div className="rounded-md bg-bg-2 px-3 py-2 text-sm text-t-2 whitespace-pre-wrap">
        {content}
      </div>
    );
  }

  const { verdict, bullets, trailing } = parsed;
  const tone = verdict === "PASS" ? "green" : verdict === "FAIL" ? "red" : "orange";
  const Icon = verdict === "PASS" ? CheckCircle2 : verdict === "FAIL" ? XCircle : AlertTriangle;
  const iconColor = verdict === "PASS" ? "text-green" : verdict === "FAIL" ? "text-red" : "text-orange";
  const accent = verdict === "PASS" ? "var(--green)" : verdict === "FAIL" ? "var(--red)" : "var(--orange)";

  return (
    <div
      className="rounded-md bg-bg-2 overflow-hidden text-sm"
      style={{ borderLeft: `3px solid ${accent}`, maxWidth: "100%" }}
    >
      <div className="flex items-center gap-2 px-3 py-2 border-b border-border">
        <Icon size={16} className={iconColor} />
        <span className="text-t-1 font-medium">Verification</span>
        <Pill label={verdict} color={tone} />
      </div>
      {bullets.length > 0 && (
        <ul className="flex flex-col gap-1 px-3 py-2">
          {bullets.map((b, i) => (
            <li key={i} className="flex items-start gap-2 text-t-2 leading-snug">
              {b.isFailure ? (
                <XCircle size={13} className="text-red flex-shrink-0 mt-0.5" />
              ) : (
                <CheckCircle2 size={13} className="text-green flex-shrink-0 mt-0.5" />
              )}
              <span>{b.text}</span>
            </li>
          ))}
        </ul>
      )}
      {trailing && (
        <div
          className="px-3 py-2 border-t border-border text-t-3 whitespace-pre-wrap"
          style={{ fontSize: 12 }}
        >
          {trailing}
        </div>
      )}
      {/* Subtle agent-color accent line at the bottom — matches SrsCard's pattern. */}
      {agentColor && (
        <div className="h-0.5" style={{ background: `color-mix(in srgb, ${agentColor} 25%, transparent)` }} />
      )}
    </div>
  );
}
