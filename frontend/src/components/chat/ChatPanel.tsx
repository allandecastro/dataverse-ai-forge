"use client";

import { Fragment, useRef, useEffect, useState, useCallback, useMemo } from "react";
import { Sparkles, Bug, Send, Square, Download, Share2, Trash2, ArrowDown, ArrowRight, Lightbulb, RotateCcw } from "lucide-react";
import { IconBtn } from "@/components/primitives/IconBtn";
import { AgentDot } from "@/components/primitives/AgentDot";
import { StreamingDots } from "@/components/primitives/StreamingDots";
import { ConfirmDialog } from "@/components/primitives/ConfirmDialog";
import { EmptyChat, EXAMPLE_PROMPTS } from "./EmptyChat";
import { MessageRenderer } from "./MessageRenderer";
import { SuggestedReplies } from "./SuggestedReplies";
import { useForgeChatContext } from "@/hooks/useForgeChat";
import { attributeToolCalls } from "@/lib/messageReducer";
import { safeParse } from "@/lib/parsing";
import { useToast } from "@/contexts/ToastContext";
import { agents, defaultAgent } from "@/lib/agents";
import type { ForgeMessage, ToolCallInfo, AgentState } from "@/lib/sse-types";

function exportChat(messages: ForgeMessage[]) {
  const lines = messages.map((m) => {
    const time = new Date(m.timestamp).toLocaleTimeString("en-US", { hour12: false });
    const role = m.role === "user" ? "You" : (agents[m.agentName ?? ""] ?? defaultAgent).displayName;
    return `**${role}** (${time})\n${m.content}\n`;
  });
  const md = `# Dataverse AI Forge — Conversation Export\n\n${lines.join("\n---\n\n")}`;
  const blob = new Blob([md], { type: "text/markdown" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `forge-conversation-${new Date().toISOString().slice(0, 10)}.md`;
  a.click();
  URL.revokeObjectURL(url);
}

/**
 * Build a complete session snapshot for sharing/debugging.
 * Includes messages, tool calls (with args/results), and agent state.
 * Secrets are NOT included — agent state only contains solution metadata.
 */
function buildSessionSnapshot(
  messages: ForgeMessage[],
  toolCalls: ToolCallInfo[],
  agentState: AgentState,
) {
  return {
    exportedAt: new Date().toISOString(),
    activeAgent: agentState.activeAgent,
    solutionState: agentState.solutionState,
    messages: messages.map((m) => ({
      id: m.id,
      role: m.role,
      timestamp: new Date(m.timestamp).toISOString(),
      agentName: m.agentName,
      content: m.content,
    })),
    toolCalls: toolCalls.map((tc) => ({
      id: tc.id,
      name: tc.name,
      messageId: tc.messageId,
      status: tc.status,
      duration: tc.endTime ? tc.endTime - tc.startTime : null,
      args: safeParse(tc.args),
      result: safeParse(tc.result ?? ""),
    })),
  };
}

// Stable reference so messages without tool calls hit MessageRenderer's memo skip path
// (a fresh `[]` literal each render would force a re-render every time).
const NO_TOOL_CALLS: ToolCallInfo[] = [];

// Distance (px) from the bottom within which we consider the user "parked" at
// the latest message and keep auto-scrolling. Beyond it, they've scrolled up to
// read and we must not yank them back down on each streamed token.
const SCROLL_PIN_THRESHOLD = 80;

/**
 * Inline divider marking an agent-to-agent handoff mid-run. Without it the
 * chat just swaps AgentDots between consecutive bubbles and the user can't tell
 * a different specialist took over. Purely presentational + derived from each
 * bubble's `agentName`, so it also renders correctly on reload; aria-hidden
 * because ChatPanel's live region announces the same transition to SR users.
 */
function HandoffDivider({ toAgent }: { toAgent: string }) {
  const meta = agents[toAgent] ?? defaultAgent;
  return (
    <div className="flex items-center gap-2 my-1 select-none" aria-hidden="true">
      <div className="flex-1 h-px bg-border" />
      <div className="flex items-center gap-1.5 text-t-4 flex-shrink-0" style={{ fontSize: 10.5 }}>
        <ArrowRight size={11} />
        <AgentDot agentName={toAgent} size={12} />
        <span>Handed off to {meta.displayName}</span>
      </div>
      <div className="flex-1 h-px bg-border" />
    </div>
  );
}

async function shareSession(
  messages: ForgeMessage[],
  toolCalls: ToolCallInfo[],
  agentState: AgentState,
) {
  const snapshot = buildSessionSnapshot(messages, toolCalls, agentState);
  const json = JSON.stringify(snapshot, null, 2);

  // Try clipboard first; fall back to download
  try {
    await navigator.clipboard.writeText(json);
    return "clipboard";
  } catch {
    const blob = new Blob([json], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `forge-session-${new Date().toISOString().slice(0, 19).replace(/:/g, "-")}.json`;
    a.click();
    URL.revokeObjectURL(url);
    return "download";
  }
}

interface ChatPanelProps {
  onToggleDiag: () => void;
  diagOpen: boolean;
  currentAgent?: string;
  /** First-run setup nudge in EmptyChat — see the corresponding EmptyChat props. */
  healthKnown?: boolean;
  azureReady?: boolean;
  dataverseReady?: boolean;
  onOpenSettings?: () => void;
}

export function ChatPanel({
  onToggleDiag,
  diagOpen,
  currentAgent,
  healthKnown,
  azureReady,
  dataverseReady,
  onOpenSettings,
}: ChatPanelProps) {
  const { messages, sendMessage, retryLastMessage, stopStreaming, isStreaming, toolCalls, agentState, clearSession } = useForgeChatContext();
  const { addToast } = useToast();
  const [confirmClearOpen, setConfirmClearOpen] = useState(false);

  // Group tool calls by messageId for inline display. Pure logic in lib/messageReducer.ts
  // (unit-tested). See comments there for the attribution strategy.
  const toolCallsByMessage = useMemo(
    () => attributeToolCalls(messages, toolCalls),
    [toolCalls, messages],
  );
  const [input, setInput] = useState("");
  // Whether the user is parked at the bottom of the transcript. Drives both the
  // auto-scroll gate and the "jump to latest" button's visibility.
  const [pinnedToBottom, setPinnedToBottom] = useState(true);
  // Reopen the starter example prompts after the first message (they otherwise
  // vanish once the transcript is non-empty).
  const [showExamples, setShowExamples] = useState(false);
  // Set after a silent stream has been running a while, to reassure the user
  // it's still working (long tool batches — table/field creates — can take 30s+).
  const [slowStream, setSlowStream] = useState(false);
  const scrollRef = useRef<HTMLDivElement>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  // Focus the composer on mount so a new user can type immediately without
  // clicking into the field first.
  useEffect(() => {
    textareaRef.current?.focus();
  }, []);

  // "Taking longer than usual" cue: flip on ~15s into a stream, reset when it
  // ends. A timer (not a token-arrival check) is intentional — the anxious case
  // is a long silent wait, which is exactly when no new tokens are arriving.
  // The reset lives in cleanup (runs when isStreaming flips or on unmount) so we
  // never call setState synchronously in the effect body.
  useEffect(() => {
    if (!isStreaming) return;
    const timer = setTimeout(() => setSlowStream(true), 15_000);
    return () => {
      clearTimeout(timer);
      setSlowStream(false);
    };
  }, [isStreaming]);

  const handleScroll = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    const distanceFromBottom = el.scrollHeight - el.scrollTop - el.clientHeight;
    setPinnedToBottom(distanceFromBottom < SCROLL_PIN_THRESHOLD);
  }, []);

  // Auto-scroll ONLY when the user is already at the bottom. Previously this
  // fired on every `messages` change unconditionally, so a user who scrolled up
  // to re-read an earlier agent got yanked back down on each streamed token.
  useEffect(() => {
    if (pinnedToBottom && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages, pinnedToBottom]);

  const scrollToBottom = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
    setPinnedToBottom(true);
  }, []);

  const handleSend = useCallback(async () => {
    const text = input.trim();
    if (!text) return;
    if (isStreaming) {
      // Don't silently drop the input: tell the user why nothing happened and
      // how to interrupt. (The Send button is already swapped for Stop, but a
      // fast typist hitting Enter never sees that.)
      addToast("The agent is still responding — press Esc or Stop to interrupt first.", "info");
      return;
    }
    setInput("");
    if (textareaRef.current) textareaRef.current.style.height = "auto";
    setPinnedToBottom(true); // a fresh send always jumps to the newest message
    await sendMessage(text);
  }, [input, isStreaming, sendMessage, addToast]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      } else if (e.key === "Escape" && isStreaming) {
        // Esc interrupts the running agent from the keyboard, matching the Stop button.
        e.preventDefault();
        stopStreaming();
      }
    },
    [handleSend, isStreaming, stopStreaming]
  );

  // Precompute each row + whether a handoff divider precedes it. A divider shows
  // when a visible assistant bubble is authored by a different agent than the
  // previous visible bubble; a user turn resets the chain (that's a new request,
  // not a handoff). Empty/tool-less placeholder bubbles are skipped so dividers
  // never bracket an invisible row.
  const renderRows = useMemo(() => {
    const rows: Array<{ msg: ForgeMessage; tools: ToolCallInfo[]; handoffTo?: string; continuation: boolean }> = [];
    let prevAgent: string | undefined;
    let prevAssistantContent: string | undefined;
    for (const msg of messages) {
      const tools = toolCallsByMessage.get(msg.id) ?? NO_TOOL_CALLS;
      const isVisibleAssistant = msg.role === "assistant" && (!!msg.content || tools.length > 0);
      // Drop an exact duplicate assistant bubble: some models re-emit their whole
      // message after calling a terminal UX tool (suggest_replies), which — now
      // that a turn is split into per-tool-boundary bubbles — surfaces as the same
      // text twice. Only skip when it's the SAME agent, identical text, and this
      // bubble carries no tools of its own (so we never hide a real tool row).
      if (
        isVisibleAssistant &&
        tools.length === 0 &&
        !!msg.content &&
        (msg.agentName ?? currentAgent) === prevAgent &&
        msg.content.trim() === prevAssistantContent?.trim()
      ) {
        continue;
      }
      let handoffTo: string | undefined;
      // A continuation is a visible assistant bubble whose agent matches the
      // previous visible bubble (same agent, no handoff) — the backend now splits
      // one agent's turn into several bubbles around tool calls, so we hide the
      // repeated header on these so a turn reads as one utterance.
      let continuation = false;
      if (msg.role === "user") {
        prevAgent = undefined;
        prevAssistantContent = undefined;
      } else if (isVisibleAssistant) {
        const agent = msg.agentName ?? currentAgent;
        if (prevAgent && agent && agent !== prevAgent) handoffTo = agent;
        else if (prevAgent && agent && agent === prevAgent) continuation = true;
        prevAgent = agent ?? prevAgent;
        if (msg.content) prevAssistantContent = msg.content;
      }
      rows.push({ msg, tools, handoffTo, continuation });
    }
    return rows;
  }, [messages, toolCallsByMessage, currentAgent]);

  // The global "thinking" indicator fills the dead-air windows the per-bubble
  // caret can't: before the first token of a turn, and between one agent's
  // TEXT_MESSAGE_END and the next agent's first token (an empty, tool-less
  // assistant bubble renders as null in MessageRenderer).
  const lastMessage = messages[messages.length - 1];
  const showThinking =
    isStreaming &&
    (!lastMessage ||
      lastMessage.role === "user" ||
      (lastMessage.role === "assistant" &&
        !lastMessage.content &&
        (toolCallsByMessage.get(lastMessage.id)?.length ?? 0) === 0));

  const activeAgentName = (agents[currentAgent ?? ""] ?? defaultAgent).displayName;
  // Concise SR announcement — changes (and thus re-announces) on handoff because
  // the agent name flips; empty when idle so nothing is read after completion.
  const liveStatus = isStreaming ? `${activeAgentName} is responding` : "";

  return (
    <section className="flex-1 min-w-0 bg-bg-1 flex flex-col">
      {/* Header */}
      <div className="flex h-12 items-center justify-between border-b border-border px-4">
        <div className="flex items-center gap-2">
          <Sparkles size={14} className="text-accent-ink" />
          <span className="text-t-1 text-sm font-semibold">Studio</span>
          {currentAgent && (
            <div className="flex items-center gap-1.5 px-2 py-0.5 border border-border bg-bg-2">
              <AgentDot agentName={currentAgent} size={14} />
              <span className="text-t-3 text-xs">
                {(agents[currentAgent] ?? defaultAgent).displayName}
              </span>
            </div>
          )}
        </div>
        <div className="flex items-center gap-1">
          {messages.length > 0 && (
            <IconBtn
              icon={Lightbulb}
              onClick={() => setShowExamples((v) => !v)}
              active={showExamples}
              title="Example prompts"
            />
          )}
          <IconBtn
            icon={Share2}
            onClick={async () => {
              const result = await shareSession(messages, toolCalls, agentState);
              addToast(
                result === "clipboard"
                  ? "Session copied to clipboard — paste it to share"
                  : "Session downloaded as JSON",
                "success",
              );
            }}
            title="Copy full session as JSON (for analysis/debugging)"
          />
          <IconBtn
            icon={Download}
            onClick={() => exportChat(messages)}
            title="Export conversation as Markdown"
          />
          <IconBtn
            icon={Trash2}
            onClick={() => setConfirmClearOpen(true)}
            title="Clear session (keeps credentials)"
          />
          <IconBtn
            icon={Bug}
            onClick={onToggleDiag}
            active={diagOpen}
            title="Toggle diagnostics"
          />
        </div>
      </div>

      {/* Messages */}
      <div className="relative flex-1 min-h-0 flex flex-col">
        {/* Screen-reader-only stream status. Sighted users see the dots/caret;
            this gives SR users the same "who is working" pulse. */}
        <div className="sr-only" role="status" aria-live="polite">
          {liveStatus}
        </div>
        <div ref={scrollRef} onScroll={handleScroll} className="flex-1 overflow-y-auto px-4 py-3 space-y-4">
          {messages.length === 0 && (
            <EmptyChat
              onPromptClick={sendMessage}
              disabled={isStreaming}
              healthKnown={healthKnown}
              azureReady={azureReady}
              dataverseReady={dataverseReady}
              onOpenSettings={onOpenSettings}
            />
          )}
          {renderRows.map(({ msg, tools, handoffTo, continuation }, idx) => (
            <Fragment key={msg.id}>
              {handoffTo && <HandoffDivider toAgent={handoffTo} />}
              <MessageRenderer
                message={msg}
                agentName={msg.agentName ?? currentAgent}
                toolCalls={tools}
                continuation={continuation}
                streaming={isStreaming && idx === renderRows.length - 1 && msg.role === "assistant" && !!msg.content}
                onRetry={msg.isError ? retryLastMessage : undefined}
                retryDisabled={isStreaming}
              />
            </Fragment>
          ))}
          {/* Suggested-reply chips surfaced by the agent's `suggest_replies` tool. */}
          {!isStreaming && (
            <SuggestedReplies
              suggestions={agentState.suggestedReplies}
              disabled={isStreaming}
              onPick={(text) => void sendMessage(text)}
            />
          )}
          {showThinking && (
            <div className="flex gap-2 items-center text-t-3 text-xs">
              <StreamingDots />
              <span>
                {slowStream
                  ? `${activeAgentName} is still working — this can take a while…`
                  : `${activeAgentName} is thinking…`}
              </span>
            </div>
          )}
          {/* Interrupted-turn recovery: a turn whose last message is the user's
              (no assistant reply) and we're not streaming means the stream was
              cut — e.g. a mid-response page reload aborted the SSE connection.
              Offer a manual Retry (never auto-resend: the interrupted turn may
              have already run a build/approval, and re-sending would duplicate it). */}
          {!isStreaming && !showThinking && messages.length > 0 &&
            messages[messages.length - 1].role === "user" && (
              <div className="flex items-center gap-2 text-xs border border-border bg-bg-2 px-3 py-2">
                <span className="flex-1 text-t-3">
                  This turn didn’t get a response — the connection may have been interrupted.
                </span>
                <button
                  onClick={() => void retryLastMessage()}
                  className="flex items-center gap-1 px-2 py-1 font-medium text-accent-ink hover:bg-bg-3 transition-colors"
                >
                  <RotateCcw size={12} />
                  Retry
                </button>
              </div>
            )}
        </div>
        {!pinnedToBottom && messages.length > 0 && (
          <button
            onClick={scrollToBottom}
            className="absolute bottom-3 left-1/2 -translate-x-1/2 flex items-center gap-1 border border-border-hi bg-bg-2 px-3 py-1 text-xs text-t-2 shadow-sm hover:bg-bg-3 transition-colors"
            aria-label="Scroll to latest message"
          >
            <ArrowDown size={12} />
            Latest
          </button>
        )}
      </div>

      {/* Input — min-h-[68px] matches the sidebar footer so the two bottom
          separators line up across columns (it grows past this as the textarea
          or example list expands). Plain block: a flex column here made the
          max-w-3xl/mx-auto input box shrink to content width. */}
      <div className="border-t border-border px-4 py-3 min-h-[68px]">
        {showExamples && messages.length > 0 && (
          <div className="max-w-3xl mx-auto mb-2 flex flex-col gap-1">
            {EXAMPLE_PROMPTS.map((prompt) => (
              <button
                key={prompt}
                onClick={() => {
                  setShowExamples(false);
                  void sendMessage(prompt);
                }}
                disabled={isStreaming}
                className="group flex items-center justify-between gap-2 border border-border bg-bg-2 px-3 py-2 text-left text-xs text-t-2 hover:border-accent-ink/40 hover:bg-bg-3 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                <span className="flex-1">{prompt}</span>
                <ArrowRight size={12} className="text-t-4 group-hover:text-accent-ink transition-colors flex-shrink-0" />
              </button>
            ))}
          </div>
        )}
        <div className="flex items-end gap-2 border border-border-hi bg-bg-1 px-3 py-2.5 max-w-3xl mx-auto transition-colors focus-within:border-accent-ink">
          <textarea
            ref={textareaRef}
            value={input}
            onChange={(e) => {
              setInput(e.target.value);
              // Auto-resize
              e.target.style.height = "auto";
              e.target.style.height = Math.min(e.target.scrollHeight, 160) + "px";
            }}
            onKeyDown={handleKeyDown}
            aria-label="Message the agents"
            placeholder={isStreaming ? "Agent is responding… press Esc to stop" : "Ask about your Dataverse solution..."}
            className="flex-1 bg-transparent text-sm text-t-1 placeholder:text-t-4 outline-none resize-none"
            rows={1}
            style={{ maxHeight: 160 }}
          />
          {isStreaming ? (
            <button
              onClick={stopStreaming}
              className="flex items-center justify-center p-1.5 bg-red text-white transition-colors flex-shrink-0 mb-0.5 hover:bg-red/80"
              title="Stop the agent"
            >
              <Square size={14} fill="currentColor" />
            </button>
          ) : (
            <button
              onClick={handleSend}
              disabled={!input.trim()}
              className="flex items-center justify-center p-1.5 transition-colors flex-shrink-0 mb-0.5 text-accent-fg disabled:text-t-4 disabled:bg-bg-3"
              style={{ background: input.trim() ? "var(--accent)" : undefined }}
              title="Send message"
            >
              <Send size={14} />
            </button>
          )}
        </div>
      </div>

      <ConfirmDialog
        open={confirmClearOpen}
        title="Clear session?"
        message={
          "This will delete all messages and tool call history for this session.\n" +
          "Your credentials and project settings will be kept."
        }
        confirmLabel="Clear"
        destructive
        onConfirm={() => {
          clearSession();
          setConfirmClearOpen(false);
          addToast("Session cleared", "success");
        }}
        onCancel={() => setConfirmClearOpen(false)}
      />
    </section>
  );
}
