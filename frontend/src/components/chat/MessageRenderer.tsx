"use client";

import type { Message } from "@copilotkit/runtime-client-gql";
import { AgentBubble } from "./AgentBubble";

interface MessageRendererProps {
  message: Message;
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

  return <AgentBubble agentName="SolutionArchitect" content={content} />;
}
