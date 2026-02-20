# Dataverse AI Forge Frontend

Next.js + React + CopilotKit (headless UI) + Tailwind CSS.

## Commands

```bash
npm run dev          # Dev server on :3000
npm run build        # Production build
npm run lint         # ESLint
npm run typecheck    # tsc --noEmit
```

## Architecture

CopilotKit is used HEADLESS — we never render <CopilotSidebar> or any CopilotKit UI components. All UI is custom.

Key hooks:
- `useCopilotChat()` → powers ChatPanel (messages, streaming, send)
- `useCopilotAction("*")` → wildcard captures all tool calls → DiagPanel
- `useCopilotAction("request_approval")` → renders ApprovalCard, blocks agent
- `useAgent()` → shared orchestrator state (phase, tables, current agent)
- `useCopilotReadable()` → sends workspace context to agents

## Design System

- Dark theme default (NeuralForge aesthetic), light theme via toggle
- Fonts: Geist (UI), JetBrains Mono (code/logical names)
- Tokens defined in `lib/themes.ts` — see spec section 10 for full values
- No emojis in UI — Lucide React icons only
- Each agent has unique icon + color (see `lib/agents.ts`)

## Component Rules

- All components are TypeScript (.tsx), functional, with explicit props interfaces
- Use Tailwind utility classes, theme tokens via CSS variables
- No inline styles except dynamic values (widths, positions)
- Primitives in `/components/primitives/` — reuse Pill, AgentDot, IconBtn, CodeBlock
- File naming: PascalCase for components, camelCase for hooks and utils