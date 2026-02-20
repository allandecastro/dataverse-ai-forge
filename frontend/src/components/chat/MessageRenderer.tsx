"use client";

import type { Message } from "@copilotkit/runtime-client-gql";
import { AgentBubble } from "./AgentBubble";
import { ThinkingTrace } from "./ThinkingTrace";

interface MessageRendererProps {
  message: Message;
}

function parseThinking(content: string): {
  reasoning: string | null;
  cleanContent: string;
} {
  const match = content.match(/<think>([\s\S]*?)<\/think>/);
  if (!match) return { reasoning: null, cleanContent: content };
  return {
    reasoning: match[1].trim(),
    cleanContent: content.replace(match[0], "").trim(),
  };
}

export function MessageRenderer({ message }: MessageRendererProps) {
  // Action execution messages (e.g. request_approval) are rendered
  // inline by CopilotKit's renderAndWaitForResponse lifecycle —
  // we just need to let them through the filter.
  if (message.isActionExecutionMessage()) {
    return null;
  }

  if (!message.isTextMessage()) return null;

  const content = message.content;
  if (!content) return null;

  if (message.role === "user") {
    return (
      <div className="flex justify-end">
        <div
          className="bg-blue-bg text-t-1 px-3 py-2 text-sm leading-relaxed max-w-[80%] whitespace-pre-wrap"
          style={{ borderRadius: "12px 12px 3px 12px" }}
        >
          {content}
        </div>
      </div>
    );
  }

  const { reasoning, cleanContent } = parseThinking(content);
  const agentName = "SolutionArchitect";

  return (
    <div>
      {reasoning && (
        <ThinkingTrace agentName={agentName} reasoning={reasoning} />
      )}
      <AgentBubble agentName={agentName} content={cleanContent} />
    </div>
  );
}
