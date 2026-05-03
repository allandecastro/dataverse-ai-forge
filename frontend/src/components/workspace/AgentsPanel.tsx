"use client";

import { useMemo, useState } from "react";
import { ArrowRight, Check, X, AlertTriangle } from "lucide-react";
import { AgentDot } from "@/components/primitives/AgentDot";
import { Pill } from "@/components/primitives/Pill";
import { agents, defaultAgent } from "@/lib/agents";
import { agentDefinitions, type AgentDefinition } from "@/lib/agentDefinitions";

type StatusFilter = "all" | "active" | "unsupported";

/**
 * Reference panel that lists every agent with its role, capabilities, and
 * boundaries — sourced from `agentDefinitions.ts`. Designed to mirror the
 * agent prompts so users can predict routing decisions before sending a
 * message. Renders inside the existing middle-panel slot (same width
 * affordance as Timeline / Activity).
 */
export function AgentsPanel() {
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("active");
  const [expanded, setExpanded] = useState<string | null>(null);

  // Sort: active first, then by display name. Unsupported agents float to the
  // bottom and only render when the user explicitly switches filter.
  const ordered = useMemo(() => {
    const list = Object.values(agentDefinitions);
    if (statusFilter !== "all") {
      return list.filter((d) => d.status === statusFilter);
    }
    return list.slice().sort((a, b) => {
      if (a.status !== b.status) return a.status === "active" ? -1 : 1;
      return (agents[a.name]?.displayName ?? a.name).localeCompare(
        agents[b.name]?.displayName ?? b.name,
      );
    });
  }, [statusFilter]);

  const counts = useMemo(() => {
    const all = Object.values(agentDefinitions);
    return {
      all: all.length,
      active: all.filter((a) => a.status === "active").length,
      unsupported: all.filter((a) => a.status === "unsupported").length,
    };
  }, []);

  return (
    <div className="h-full overflow-y-auto p-4">
      <div className="mb-3">
        <h2 className="text-t-1 text-base font-semibold mb-1">Agents</h2>
        <p className="text-t-3 text-xs leading-relaxed">
          Each agent owns a slice of the workflow. The Architect routes work; specialists
          do it; the Reviewer / Tester check it. Click an agent to see its full boundaries.
        </p>
      </div>

      <div className="flex items-center gap-1 mb-3">
        <FilterChip
          label={`Active (${counts.active})`}
          active={statusFilter === "active"}
          onClick={() => setStatusFilter("active")}
        />
        <FilterChip
          label={`All (${counts.all})`}
          active={statusFilter === "all"}
          onClick={() => setStatusFilter("all")}
        />
        <FilterChip
          label={`Not supported (${counts.unsupported})`}
          active={statusFilter === "unsupported"}
          onClick={() => setStatusFilter("unsupported")}
        />
      </div>

      <div className="flex flex-col gap-2">
        {ordered.map((def) => (
          <AgentCard
            key={def.name}
            def={def}
            isExpanded={expanded === def.name}
            onToggle={() => setExpanded((prev) => (prev === def.name ? null : def.name))}
          />
        ))}
      </div>
    </div>
  );
}

function FilterChip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={`px-2.5 py-0.5 rounded-full text-xs font-medium transition-colors ${
        active ? "bg-blue text-white" : "bg-bg-3 text-t-3 hover:text-t-2"
      }`}
    >
      {label}
    </button>
  );
}

function AgentCard({
  def,
  isExpanded,
  onToggle,
}: {
  def: AgentDefinition;
  isExpanded: boolean;
  onToggle: () => void;
}) {
  const meta = agents[def.name] ?? defaultAgent;
  const Icon = meta.icon;
  const unsupported = def.status === "unsupported";

  return (
    <div
      className={`rounded-lg border bg-bg-1 overflow-hidden transition-colors ${
        unsupported ? "border-border opacity-70" : "border-border"
      }`}
    >
      <button
        onClick={onToggle}
        className="w-full flex items-start gap-3 px-3 py-2.5 text-left hover:bg-bg-2 transition-colors"
        aria-expanded={isExpanded}
      >
        <div
          className="flex-shrink-0 mt-0.5 w-7 h-7 rounded-md flex items-center justify-center"
          style={{ background: `color-mix(in srgb, ${meta.color} 15%, transparent)` }}
        >
          <Icon size={14} style={{ color: meta.color }} />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 mb-0.5">
            <span className="text-t-1 text-sm font-medium">{meta.displayName}</span>
            {unsupported ? (
              <Pill label="not supported" color="neutral" />
            ) : (
              <Pill label="active" color="green" />
            )}
          </div>
          <p className="text-t-3 text-xs leading-snug">{def.role}</p>
        </div>
      </button>

      {isExpanded && (
        <div className="px-3 pb-3 pt-1 flex flex-col gap-3 text-xs border-t border-border">
          {unsupported && def.unsupportedNote && (
            <div className="flex items-start gap-2 rounded-md bg-bg-2 px-2.5 py-2">
              <AlertTriangle size={12} className="text-orange flex-shrink-0 mt-0.5" />
              <span className="text-t-2 leading-snug">{def.unsupportedNote}</span>
            </div>
          )}

          {def.canDo.length > 0 && (
            <Section title="Can do" tone="green">
              {def.canDo.map((line, i) => (
                <BulletRow key={i} icon={<Check size={11} className="text-green" />} text={line} />
              ))}
            </Section>
          )}

          {def.cannotDo.length > 0 && (
            <Section title="Cannot do" tone="red">
              {def.cannotDo.map((line, i) => (
                <BulletRow key={i} icon={<X size={11} className="text-red" />} text={line} />
              ))}
            </Section>
          )}

          {def.handsOffTo.length > 0 && (
            <Section title="Hands off to" tone="neutral">
              <div className="flex flex-wrap gap-1.5">
                {def.handsOffTo.map((target) => (
                  <HandoffChip key={target} agentName={target} />
                ))}
              </div>
            </Section>
          )}

          {def.receivesFrom.length > 0 && (
            <Section title="Receives from" tone="neutral">
              <div className="flex flex-wrap gap-1.5">
                {def.receivesFrom.map((src) => (
                  <HandoffChip key={src} agentName={src} />
                ))}
              </div>
            </Section>
          )}
        </div>
      )}
    </div>
  );
}

function Section({
  title,
  tone,
  children,
}: {
  title: string;
  tone: "green" | "red" | "neutral";
  children: React.ReactNode;
}) {
  const dotColor = tone === "green" ? "bg-green" : tone === "red" ? "bg-red" : "bg-t-4";
  return (
    <div>
      <div className="flex items-center gap-1.5 mb-1.5">
        <span className={`w-1 h-1 rounded-full ${dotColor}`} />
        <span className="text-t-3 font-medium uppercase tracking-wide" style={{ fontSize: 10 }}>
          {title}
        </span>
      </div>
      <div className="flex flex-col gap-1 pl-2.5">{children}</div>
    </div>
  );
}

function BulletRow({ icon, text }: { icon: React.ReactNode; text: string }) {
  return (
    <div className="flex items-start gap-1.5">
      <span className="flex-shrink-0 mt-0.5">{icon}</span>
      <span className="text-t-2 leading-snug">{text}</span>
    </div>
  );
}

/**
 * Renders a handoff target as either an agent dot + name (when it matches a
 * known agent) or as a plain label ("User", "Every other agent…"). The dot
 * lookup means free-form strings like "User" don't crash.
 */
function HandoffChip({ agentName }: { agentName: string }) {
  const known = agents[agentName];
  if (!known) {
    return (
      <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-bg-2 text-t-3" style={{ fontSize: 11 }}>
        <ArrowRight size={10} />
        {agentName}
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-bg-2 text-t-2" style={{ fontSize: 11 }}>
      <AgentDot agentName={agentName} size={12} />
      {known.displayName}
    </span>
  );
}
