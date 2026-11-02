# Dataverse AI Forge — Demo Script (10 min)

Presenter guide for demonstrating the platform.

---

## Pre-Demo Setup

1. **Backend**: `cd backend && dotnet run --project src/DataverseForge.Api`
2. **Frontend**: `cd frontend && npm run dev`
3. **URLs**: `http://localhost:3000` (normal) / `http://localhost:3000?demo=true` (fallback)
4. **Theme**: Start in dark mode (default)
5. **Browser**: Chrome/Edge, zoom 100%, hide bookmarks bar
6. **Fallback**: If the backend fails, use `?demo=true` — full UI with pre-populated data, no backend needed.

---

## Demo Segments

### Segment 1 — Opening (0:00 - 1:00)

**Action**: Open `http://localhost:3000`. Create a new project or select existing.

**Talking points**:
- "Dataverse AI Forge uses 8 specialized AI agents to build Dataverse solutions through conversation"
- "Each agent has a specific role — architect, analyst, schema builder, UI builder, data populator, tester, security builder, automation builder"
- "Let's build a feedback management system for Contoso"

---

### Segment 2 — First Message (1:00 - 2:00)

**Action**: Type: "I need a feedback management system for Contoso. Customers submit feedback with ratings and categories, and our team responds to each one."

**Layout**: Sidebar | Chat (full width)

**Talking points**:
- "The Solution Architect receives the request and decides which specialist to involve"
- Point out the agent badge in the chat header and the thinking trace
- "Each agent can use a different model — GPT-4o for the Architect, Llama for the Builder"

---

### Segment 3 — BA Elicitation (2:00 - 3:00)

**Action**: Answer BA questions: "Categories: Product, Service, Support, General. Rating 1-5. Track responses. Status: New, Under Review, Responded, Closed."

**Talking points**:
- "The Business Analyst asks clarifying questions — like a real requirements session"
- "Notice the agent badge changed — that's the BA"
- Show inline tool calls under agent messages

---

### Segment 4 — SRS Card (3:00 - 4:00)

**Action**: Wait for BA to generate the SRS card. Expand sections.

**Talking points**:
- "The BA produced a structured requirement specification from a natural conversation"
- "Entities, business rules, relationships, security — all structured"

---

### Segment 5 — Approval Card (4:00 - 5:30)

**Action**: Review the ApprovalCard for table creation. Click "Approve".

**Talking points**:
- "Human-in-the-loop: the Builder shows exactly what it plans to create"
- "Risk level is orange — creating a new table"
- "Expand details to see the OData payload — click Approve"
- Show the inline tool calls: "You can see every tool call right under the message"

---

### Segment 6 — Creation + Verification (5:30 - 7:00)

**Action**: Watch tables being created. Show inline tool call details expanding.

**Talking points**:
- "The Tester automatically re-reads everything to verify"
- "PASS on all fields, types, and relationships"
- "Every tool call is shown inline — expand to see args and results"

---

### Segment 7 — Timeline (7:00 - 8:00)

**Action**: Click "Timeline" in sidebar to open middle panel.

**Talking points**:
- "Timeline shows a Gantt chart of agent activity"
- "Color-coded by agent — click an agent name to filter diagnostics"
- "Click a bar to jump to that specific tool call in the diagnostics panel"
- Resize the panel by dragging the right edge

---

### Segment 8 — Token Panel + Wrap-up (8:00 - 9:30)

**Action**: Click the token pill in the sidebar footer.

**Talking points**:
- "Per-agent breakdown: which agents used which models, how many tokens"
- "Total cost: under $0.50 for the entire solution"
- "Manual equivalent: a consultant would charge ~$4,800"

---

### Segment 9 — Theme + Close (9:30 - 10:00)

**Action**: Toggle to light theme via sidebar. Show export button.

**Talking points**:
- "Full light/dark theme support"
- "Export the entire conversation as markdown"
- "Chat history persists across sessions"
- "9 agents, full governance, any model via Azure AI Foundry. Questions?"

---

## Fallback: Demo Mode

If the backend is unavailable:

1. Navigate to `http://localhost:3000?demo=true`
2. Full UI with pre-populated "Contoso Feedback" session data
3. All views work: Timeline, Activity Log, Token Panel
4. Chat shows pre-recorded messages with SRS card and ApprovalCard
5. Theme toggle works normally
6. To exit: click "Switch Project" or navigate to `/`
