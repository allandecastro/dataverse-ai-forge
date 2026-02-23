"use client";

interface BpfStage {
  name: string;
  steps: { name: string; required: boolean }[];
}

interface BpfVisualizerProps {
  stageFlowDiagram: string;
}

function parseStageFlow(diagram: string): BpfStage[] {
  // Parse format: [StageName: *requiredField, optionalField] → [StageName2: ...]
  const stageStrings = diagram.split(/\s*→\s*/);
  return stageStrings
    .map((s) => {
      const match = s.match(/\[([^:]+)(?::\s*(.+))?\]/);
      if (!match) return null;
      const name = match[1].trim();
      const stepsStr = match[2]?.trim() ?? "";
      const steps = stepsStr
        .split(/,\s*/)
        .filter(Boolean)
        .map((step) => {
          const required = step.startsWith("*");
          return {
            name: required ? step.slice(1).trim() : step.trim(),
            required,
          };
        });
      return { name, steps };
    })
    .filter((s): s is BpfStage => s !== null);
}

export function BpfVisualizer({ stageFlowDiagram }: BpfVisualizerProps) {
  const stages = parseStageFlow(stageFlowDiagram);

  if (stages.length === 0) {
    return (
      <div className="text-t-3 text-xs">Unable to parse BPF stages</div>
    );
  }

  return (
    <div className="flex items-start gap-1 overflow-x-auto py-2">
      {stages.map((stage, i) => (
        <div key={stage.name} className="flex items-center gap-1">
          {/* Stage box */}
          <div className="rounded-lg border border-border bg-bg-2 px-3 py-2 min-w-[120px]">
            <div
              className="text-t-1 text-xs font-medium mb-1.5"
              style={{ fontFamily: "var(--font-geist-mono), monospace" }}
            >
              {stage.name}
            </div>
            {stage.steps.length > 0 && (
              <ul className="space-y-0.5">
                {stage.steps.map((step) => (
                  <li
                    key={step.name}
                    className="text-xs flex items-center gap-1"
                  >
                    {step.required ? (
                      <span className="text-orange text-[10px]">*</span>
                    ) : (
                      <span className="text-t-4 text-[10px]">&bull;</span>
                    )}
                    <span className={step.required ? "text-t-1 font-medium" : "text-t-3"}>
                      {step.name}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {/* Arrow connector */}
          {i < stages.length - 1 && (
            <svg width="20" height="20" viewBox="0 0 20 20" className="flex-shrink-0">
              <path
                d="M4 10 L14 10 M10 6 L14 10 L10 14"
                fill="none"
                stroke="var(--t4)"
                strokeWidth="1.5"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </svg>
          )}
        </div>
      ))}
    </div>
  );
}
