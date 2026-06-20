# Dataverse AI Forge Frontend

Next.js + React + Custom SSE Client + Tailwind CSS.

## Commands

```bash
npm run dev          # Dev server on :3000
npm run build        # Production build
npm run lint         # ESLint
npm run typecheck    # tsc --noEmit
```

## Architecture

All chat communication uses a custom SSE client that connects directly to the .NET backend via `/api/chat` proxy route. No third-party chat frameworks.

Key hooks:
- `useForgeChatContext()` → powers ChatPanel (messages, streaming, send, agentState, toolCalls)
- `useAgent()` → derives phase, tables, current agent from `agentState`
- `useDiagnostics()` → derives DiagnosticEntry[] from `toolCalls`
- `useForgeApprovals()` → detects `request_approval` tool calls, renders ApprovalCard
- `useTokenTracker()` → accumulates per-agent token/cost data from RUN_FINISHED metadata

SSE infrastructure:
- `lib/sse-types.ts` → typed SSE event definitions, ForgeMessage, ToolCallInfo, AgentState
- `lib/sse-parser.ts` → async generator consuming ReadableStream into typed events
- `hooks/useForgeChat.tsx` → ForgeChatProvider context + sendMessage + SSE stream processing

## Design System

- Dark theme default (NeuralForge aesthetic), light theme via toggle
- Fonts: IBM Plex Sans (UI), IBM Plex Mono (code/logical names) — loaded in `app/layout.tsx`, exposed as `--font-plex-sans` / `--font-plex-mono`
- Tokens defined in `lib/themes.ts` — see spec section 10 for full values
- No emojis in UI — Lucide React icons only
- Each agent has unique icon + color (see `lib/agents.ts`)

## Component Rules

- All components are TypeScript (.tsx), functional, with explicit props interfaces
- Use Tailwind utility classes, theme tokens via CSS variables
- No inline styles except dynamic values (widths, positions)
- Primitives in `/components/primitives/` — reuse Pill, AgentDot, IconBtn, CodeBlock
- File naming: PascalCase for components, camelCase for hooks and utils
