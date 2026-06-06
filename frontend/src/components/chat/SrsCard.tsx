"use client";

import { useState } from "react";
import ReactMarkdown from "react-markdown";
import { ChevronRight, ChevronDown, FileText } from "lucide-react";
import { Pill } from "@/components/primitives/Pill";

interface SrsSection {
  title: string;
  content: string;
  level: number; // 2 for ##, 3 for ###
}

interface SrsCardProps {
  content: string;
  agentColor: string;
}

function parseSections(content: string): SrsSection[] {
  const sections: SrsSection[] = [];
  const lines = content.split("\n");
  let current: SrsSection | null = null;

  for (const line of lines) {
    const h2Match = line.match(/^## (.+)/);
    const h3Match = line.match(/^### (.+)/);

    if (h2Match || h3Match) {
      if (current) sections.push(current);
      current = {
        title: (h2Match?.[1] ?? h3Match?.[1] ?? "").trim(),
        content: "",
        level: h2Match ? 2 : 3,
      };
    } else if (current) {
      current.content += line + "\n";
    }
  }
  if (current) sections.push(current);
  return sections;
}

/** Extract entity names from content (words that look like PascalCase logical names) */
function extractEntities(content: string): string[] {
  const matches = content.match(/\b[A-Z][a-z]+(?:[A-Z][a-z]+)+\b/g);
  return [...new Set(matches ?? [])];
}

function SrsSection({ section }: { section: SrsSection }) {
  const [open, setOpen] = useState(section.level === 2);
  const trimmed = section.content.trim();
  const entities = section.title.toLowerCase().includes("entit") ? extractEntities(trimmed) : [];

  return (
    <div className={section.level === 2 ? "" : "ml-3"}>
      <button
        onClick={() => setOpen(!open)}
        className="flex items-center gap-1.5 text-t-2 text-xs font-medium hover:text-t-1 w-full text-left py-1"
      >
        {open ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
        {section.title}
      </button>
      {open && (
        <div className="ml-4 pb-1">
          {entities.length > 0 && (
            <div className="flex flex-wrap gap-1 mb-1.5">
              {entities.map((e) => (
                <Pill key={e} label={e} color="blue" />
              ))}
            </div>
          )}
          <div className="text-t-3 text-xs leading-relaxed">
            <ReactMarkdown
              components={{
                p: ({ children }) => <p className="mb-1.5 last:mb-0">{children}</p>,
                strong: ({ children }) => <strong className="font-semibold text-t-1">{children}</strong>,
                em: ({ children }) => <em className="italic">{children}</em>,
                ul: ({ children }) => <ul className="list-disc list-inside mb-1.5 last:mb-0 space-y-0.5">{children}</ul>,
                ol: ({ children }) => <ol className="list-decimal list-inside mb-1.5 last:mb-0 space-y-0.5">{children}</ol>,
                li: ({ children }) => <li className="text-t-3">{children}</li>,
                code: ({ children }) => (
                  <code className="bg-bg-1 rounded px-1 py-0.5 text-[11px] text-t-1 font-mono">
                    {children}
                  </code>
                ),
                a: ({ children, href }) => <a href={href} className="text-blue underline hover:no-underline">{children}</a>,
              }}
            >
              {trimmed}
            </ReactMarkdown>
          </div>
        </div>
      )}
    </div>
  );
}

export function SrsCard({ content, agentColor }: SrsCardProps) {
  const sections = parseSections(content);

  return (
    <div
      className="rounded-lg border border-border bg-bg-2 overflow-hidden"
      style={{ borderLeft: `3px solid ${agentColor}` }}
    >
      <div className="flex items-center gap-2 px-3 py-2 border-b border-border-lt">
        <FileText size={14} style={{ color: agentColor }} />
        <span className="text-t-1 text-sm font-medium">Requirement Specification</span>
      </div>
      <div className="px-3 py-2 space-y-0.5 max-h-80 overflow-y-auto">
        {sections.map((section, i) => (
          <SrsSection key={`${section.title}-${i}`} section={section} />
        ))}
      </div>
    </div>
  );
}

/** Check if content looks like an SRS document from the BA */
export function isSrsContent(content: string): boolean {
  // Must contain at least 2 markdown headers and look like structured specs
  const headerCount = (content.match(/^#{2,3} /gm) ?? []).length;
  return headerCount >= 2;
}
