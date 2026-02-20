# Dataverse AI Forge

AI-powered platform that builds Microsoft Dataverse solutions through natural language conversation. 10 specialized agents collaborate to design, validate, create, test, document, and secure solutions.

## Architecture

- Backend: .NET 9 Clean Architecture in `/backend` (DVForge.slnx)
- Frontend: Next.js + CopilotKit (headless) in `/frontend`
- Bridge: AG-UI protocol (SSE) between backend and frontend
- Agents: Microsoft Agent Framework with handoff orchestration
- LLM: Azure OpenAI (GPT-4o + GPT-4o-mini)
- Spec: Full specification in `/docs/dataverse-ai-forge-spec-v4.md`

## Build & Test

```bash
# Backend
cd backend && dotnet build DVForge.slnx && dotnet test DVForge.slnx

# Frontend
cd frontend && npm install && npm run build && npm run lint

# Both (docker)
docker-compose up --build
```

## Key Conventions

- Always read the spec in `/docs/dataverse-ai-forge-spec-v4.md` before implementing a feature
- Backend follows Clean Architecture: Domain has zero dependencies, Application references Domain only, Infrastructure implements interfaces
- Every agent tool follows the plan_*/execute_* split pattern
- Every write operation requires an entry in the operation stack for undo support
- Use central package management (Directory.Packages.props) — never pin versions in individual .csproj
- Commit after each working unit with conventional commits: `feat:`, `fix:`, `refactor:`, `docs:`

## Progress

See `/docs/phase-checklist.md` for current implementation status.