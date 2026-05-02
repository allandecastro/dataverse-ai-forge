"use client";

import { useRef, useEffect } from "react";
import Link from "next/link";
import { Sparkles, Bug } from "lucide-react";
import { IconBtn } from "@/components/primitives/IconBtn";
import { AgentBubble } from "./AgentBubble";
import { ThinkingTrace } from "./ThinkingTrace";
import { SrsCard, isSrsContent } from "./SrsCard";
import { ApprovalCard } from "./ApprovalCard";
import { AgentDot } from "@/components/primitives/AgentDot";
import { agents, defaultAgent } from "@/lib/agents";
import { demoChatMessages, type DemoChatMessage } from "@/lib/demo-scenario";

interface DemoChatPanelProps {
  onToggleDiag: () => void;
  diagOpen: boolean;
}

function DemoMessage({ msg }: { msg: DemoChatMessage }) {
  if (msg.role === "user") {
    return (
      <div className="flex justify-end">
        <div
          className="bg-blue-bg text-t-1 px-3 py-2 text-sm leading-relaxed max-w-[80%] whitespace-pre-wrap"
          style={{ borderRadius: "12px 12px 3px 12px" }}
        >
          {msg.content}
        </div>
      </div>
    );
  }

  const agentName = msg.agentName ?? "SolutionArchitect";
  const agent = agents[agentName] ?? defaultAgent;

  if (msg.approval) {
    return (
      <div>
        {msg.thinking && (
          <ThinkingTrace agentName={agentName} reasoning={msg.thinking} />
        )}
        <div className="flex gap-2 max-w-[90%]">
          <AgentDot agentName={agentName} />
          <div className="flex flex-col gap-1 flex-1">
            <span className="text-t-3" style={{ fontSize: 11 }}>
              {agent.displayName}
            </span>
            {msg.content && (
              <div
                className="bg-bg-2 text-t-2 px-3 py-2 text-sm leading-relaxed whitespace-pre-wrap mb-2"
                style={{ borderRadius: "3px 12px 12px 12px" }}
              >
                {msg.content}
              </div>
            )}
            <ApprovalCard
              operationType={msg.approval.operationType}
              description={msg.approval.description}
              riskLevel={msg.approval.riskLevel}
              details={msg.approval.details}
              status={msg.approval.status}
            />
          </div>
        </div>
      </div>
    );
  }

  if (msg.isSrs && isSrsContent(msg.content)) {
    return (
      <div>
        {msg.thinking && (
          <ThinkingTrace agentName={agentName} reasoning={msg.thinking} />
        )}
        <div className="flex gap-2 max-w-[90%]">
          <AgentDot agentName={agentName} />
          <div className="flex flex-col gap-1 flex-1">
            <span className="text-t-3" style={{ fontSize: 11 }}>
              {agent.displayName}
            </span>
            <SrsCard content={msg.content} agentColor={agent.color} />
          </div>
        </div>
      </div>
    );
  }

  return (
    <div>
      {msg.thinking && (
        <ThinkingTrace agentName={agentName} reasoning={msg.thinking} />
      )}
      <AgentBubble agentName={agentName} content={msg.content} />
    </div>
  );
}

export function DemoChatPanel({ onToggleDiag, diagOpen }: DemoChatPanelProps) {
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, []);

  return (
    <section className="flex-1 min-w-0 bg-bg-1 flex flex-col">
      {/* Header */}
      <div className="flex h-12 items-center justify-between border-b border-border px-4">
        <div className="flex items-center gap-2">
          <Sparkles size={14} className="text-purple" />
          <span className="text-t-2 text-sm font-medium">Assistant</span>
        </div>
        <div className="flex items-center gap-1">
          <IconBtn
            icon={Bug}
            onClick={onToggleDiag}
            active={diagOpen}
            title="Toggle diagnostics"
          />
        </div>
      </div>

      {/* Messages */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto px-4 py-3 space-y-4">
        {demoChatMessages.map((msg) => (
          <DemoMessage key={msg.id} msg={msg} />
        ))}
      </div>

      {/* Demo mode indicator */}
      <div className="border-t border-border px-4 py-3">
        <div className="flex items-center justify-center gap-3 rounded-lg border border-border bg-bg-2 px-3 py-2 max-w-3xl mx-auto">
          <Sparkles size={14} className="text-purple flex-shrink-0" />
          <span className="text-t-3 text-sm">
            Demo Mode — this is a recorded scenario, not a live agent run.
          </span>
          <Link
            href="/"
            className="text-blue text-sm hover:underline flex-shrink-0"
            title="Exit demo and return to the real app"
          >
            Exit
          </Link>
        </div>
      </div>
    </section>
  );
}
