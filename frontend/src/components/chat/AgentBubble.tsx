"use client";

import { useState, useCallback, useDeferredValue, type ReactNode } from "react";
import ReactMarkdown from "react-markdown";
import { Copy, Check, Download, ExternalLink } from "lucide-react";
import { agents, defaultAgent } from "@/lib/agents";
import { AgentDot } from "@/components/primitives/AgentDot";

interface AgentBubbleProps {
  agentName: string;
  content: string;
  timestamp?: number;
}

/**
 * Renders a download-card affordance for solution-export links the backend
 * emits as `[Download solution.zip](http://.../api/exports/solution.zip)`.
 * Non-export links fall through to a normal underlined anchor with an
 * external-link glyph.
 *
 * Detection key: any URL whose path contains `/api/exports/` — that's the
 * route the backend's ExportsEndpoints serves with Content-Disposition: attachment.
 */
function isExportsLink(href: string | undefined): boolean {
  if (!href) return false;
  // Tolerate both absolute URLs (http://localhost:5000/api/exports/...) and
  // any future same-origin relative path (/api/exports/...).
  return /\/api\/exports\//i.test(href);
}

function DownloadCard({ href, children }: { href: string; children: ReactNode }) {
  // The link text the agent wrote (e.g. "Download FleetOps_unmanaged.zip").
  // Fall back to the filename from the URL if the markdown link text is empty
  // or a placeholder.
  const filenameFromUrl = (() => {
    try {
      const last = href.split("?")[0].split("#")[0].split("/").pop() ?? "";
      return decodeURIComponent(last);
    } catch {
      return "";
    }
  })();
  // The anchor is rendered INSIDE a markdown <p>, so its children must be
  // valid phrasing content. Past failure: a <div> inside the <a> triggered a
  // React hydration error ("<div> cannot be a descendant of <p>"). Use <span>
  // with display: inline-flex / inline-block so the layout still looks like a
  // card while the markup stays inline-level.
  return (
    <a
      href={href}
      download={filenameFromUrl || undefined}
      className="not-prose inline-flex items-center gap-2.5 my-1 px-3 py-2 rounded-md bg-bg-1 border border-border hover:bg-bg-3 hover:border-blue transition-colors no-underline align-middle"
      style={{ textDecoration: "none" }}
      target="_blank"
      rel="noopener noreferrer"
    >
      <Download size={16} className="text-blue flex-shrink-0" />
      <span className="inline-flex flex-col min-w-0 leading-tight">
        <span className="text-t-1 font-medium text-sm truncate" style={{ fontFamily: "var(--font-geist-mono), monospace" }}>
          {filenameFromUrl || children}
        </span>
        <span className="text-t-3" style={{ fontSize: 11 }}>
          Click to download
        </span>
      </span>
    </a>
  );
}

export function AgentBubble({ agentName, content, timestamp }: AgentBubbleProps) {
  const agent = agents[agentName] ?? defaultAgent;
  const [copied, setCopied] = useState(false);

  // Defer the markdown reparse during streaming. Each TEXT_MESSAGE_CONTENT chunk
  // triggers a state update; without useDeferredValue React would re-run
  // react-markdown synchronously per chunk (e.g., 200 reparses for a 200-line
  // agent reply). useDeferredValue lets React batch markdown work behind a
  // priority barrier so user-visible interactions stay snappy.
  const deferredContent = useDeferredValue(content);

  const handleCopy = useCallback(() => {
    navigator.clipboard.writeText(content);
    setCopied(true);
    setTimeout(() => setCopied(false), 1500);
  }, [content]);

  return (
    <div className="flex gap-2 max-w-[90%] group">
      <AgentDot agentName={agentName} />
      <div className="flex flex-col gap-1 flex-1 min-w-0">
        <div className="flex items-center gap-2">
          <span className="text-t-3" style={{ fontSize: 11 }}>
            {agent.displayName}
          </span>
          {timestamp && (
            <span className="text-t-4" style={{ fontSize: 10, fontFamily: "var(--font-geist-mono), monospace" }}>
              {new Date(timestamp).toLocaleTimeString("en-US", { hour12: false, hour: "2-digit", minute: "2-digit" })}
            </span>
          )}
          <button
            onClick={handleCopy}
            className="opacity-0 group-hover:opacity-100 transition-opacity ml-auto p-0.5 rounded hover:bg-bg-3 text-t-4 hover:text-t-2"
            title="Copy message"
          >
            {copied ? <Check size={12} className="text-green" /> : <Copy size={12} />}
          </button>
        </div>
        <div
          className="bg-bg-2 text-t-2 px-3 py-2 text-sm leading-relaxed overflow-hidden"
          style={{ borderRadius: "3px 12px 12px 12px" }}
        >
          <ReactMarkdown
            components={{
              // Render markdown paragraphs as <div> instead of <p>. HTML's
              // "no block descendants of <p>" rule made any block-level child
               // (download cards, code blocks, future inline cards) a hydration
              // error. <div> with the same vertical-rhythm class is visually
              // identical and accepts any descendant. We don't depend on
              // paragraph semantics for screen-reader / SEO in a chat UI.
              p: ({ children }) => <div className="mb-2 last:mb-0">{children}</div>,
              strong: ({ children }) => <strong className="font-semibold text-t-1">{children}</strong>,
              em: ({ children }) => <em className="italic">{children}</em>,
              ul: ({ children }) => <ul className="list-disc list-inside mb-2 last:mb-0">{children}</ul>,
              ol: ({ children }) => <ol className="list-decimal list-inside mb-2 last:mb-0">{children}</ol>,
              li: ({ children }) => <li className="mb-0.5">{children}</li>,
              code: ({ children, className }) => {
                const isBlock = className?.includes("language-");
                if (isBlock) {
                  return (
                    <pre className="bg-bg-1 rounded px-2 py-1.5 my-1 overflow-x-auto text-xs" style={{ fontFamily: "var(--font-geist-mono), monospace" }}>
                      <code>{children}</code>
                    </pre>
                  );
                }
                return (
                  <code className="bg-bg-1 rounded px-1 py-0.5 text-xs text-t-1" style={{ fontFamily: "var(--font-geist-mono), monospace" }}>
                    {children}
                  </code>
                );
              },
              pre: ({ children }) => <>{children}</>,
              h1: ({ children }) => <h1 className="text-base font-semibold text-t-1 mb-1 mt-2">{children}</h1>,
              h2: ({ children }) => <h2 className="text-sm font-semibold text-t-1 mb-1 mt-2">{children}</h2>,
              h3: ({ children }) => <h3 className="text-sm font-medium text-t-1 mb-1 mt-1">{children}</h3>,
              table: ({ children }) => <table className="w-full text-xs border-collapse my-2">{children}</table>,
              th: ({ children }) => <th className="text-left px-2 py-1 border border-border bg-bg-1 text-t-2 font-medium">{children}</th>,
              td: ({ children }) => <td className="px-2 py-1 border border-border text-t-3">{children}</td>,
              blockquote: ({ children }) => <blockquote className="border-l-2 border-border pl-3 my-1 text-t-3 italic">{children}</blockquote>,
              hr: () => <hr className="border-border my-2" />,
              a: ({ href, children }) => {
                if (isExportsLink(href)) {
                  return <DownloadCard href={href!}>{children}</DownloadCard>;
                }
                // Default link styling — underlined blue with an external-link
                // glyph so users know it'll open in a new tab.
                return (
                  <a
                    href={href}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-blue hover:underline inline-flex items-center gap-0.5"
                  >
                    {children}
                    <ExternalLink size={11} className="opacity-60" />
                  </a>
                );
              },
            }}
          >
            {deferredContent}
          </ReactMarkdown>
        </div>
      </div>
    </div>
  );
}
