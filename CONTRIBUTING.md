# Contributing

Thanks for your interest! Dataverse AI Forge is an **example project** demonstrating
multi-agent orchestration (Microsoft Agent Framework) and the AG-UI streaming protocol
against Microsoft Dataverse. Contributions that improve clarity, correctness, or the
learning value are very welcome.

By participating you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## Ground rules

- Keep changes focused — one concern per pull request.
- The **code is the source of truth**; there is no separate spec. Before changing an
  agent's behavior, read its prompt in
  `backend/src/DataverseForge.Application/Agents/Prompts/*.md` and the relevant tool.
- Out-of-scope capabilities are listed in [UNSUPPORTED.md](UNSUPPORTED.md) — please don't
  reintroduce them without discussion.

## Getting set up

Prerequisites: **.NET 10 SDK**, **Node.js 22+**.

```bash
# Backend
cd backend
dotnet build DataverseForge.slnx
dotnet test DataverseForge.slnx
dotnet run --project src/DataverseForge.Api

# Frontend
cd frontend
npm install
npm run build
npm run lint
npm test
npm run dev
```

To run the full flow you'll need your own Azure AI Foundry endpoint and a **throwaway**
Dataverse environment — enter them in the in-app Settings (gear icon). See the README's
"Just here to test it?" section. Never point the app at a production org.

## Before you open a PR

- Backend: `dotnet build` **and** `dotnet test` are green.
- Frontend: `npm run build`, `npm run lint`, and `npm test` are green.
- Follow the existing conventions:
  - Clean Architecture layering (`Domain` → `Application` → `Infrastructure` → `Api`;
    `Domain` has zero dependencies).
  - The `plan_*` / `execute_*` tool split for Dataverse writes.
  - Central package management — never pin versions in individual `.csproj`; edit
    `backend/Directory.Packages.props`.
- Use conventional commit messages: `feat:`, `fix:`, `refactor:`, `docs:`.
- If your change touches an agent flow, exercise it end to end. Passing unit tests is not
  the same as a working user flow — see the recurring-bug notes in [CLAUDE.md](CLAUDE.md).

## Architecture at a glance

- **Backend** — .NET 10 Clean Architecture in `/backend`.
- **Frontend** — Next.js + a custom SSE client (`ForgeChatProvider`) in `/frontend`.
- **Bridge** — AG-UI protocol (Server-Sent Events) between the two.

Deeper conventions live in [CLAUDE.md](CLAUDE.md), [backend/CLAUDE.md](backend/CLAUDE.md),
and [frontend/CLAUDE.md](frontend/CLAUDE.md). These double as the AI-assistant
instructions the project is built with — a useful window into how it's structured.
