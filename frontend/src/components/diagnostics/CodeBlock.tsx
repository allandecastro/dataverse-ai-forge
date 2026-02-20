"use client";

interface CodeBlockProps {
  data: unknown;
  maxHeight?: number;
}

export function CodeBlock({ data, maxHeight = 180 }: CodeBlockProps) {
  const text =
    typeof data === "string" ? data : JSON.stringify(data, null, 2) ?? "null";

  return (
    <pre
      className="overflow-auto rounded bg-code-bg text-code-text text-xs p-2 whitespace-pre-wrap"
      style={{ maxHeight, fontFamily: "var(--font-geist-mono), monospace" }}
    >
      {text}
    </pre>
  );
}
