# Dataverse AI Forge — Demo Script (10 min)

Presenter guide for demonstrating the full platform. Based on spec Section 13.

---

## Pre-Demo Setup

1. **Backend**: `cd backend && dotnet run --project src/DataverseForge.Api`
2. **Frontend**: `cd frontend && npm run dev`
3. **URLs**: `http://localhost:3000` (normal) / `http://localhost:3000?demo=true` (fallback)
4. **Theme**: Start in dark mode (default)
5. **Browser**: Use Chrome/Edge, zoom to 100%, hide bookmarks bar
6. **Fallback plan**: If the backend fails to start, append `?demo=true` to the URL. This renders the full UI with pre-populated data and no backend dependency.

---

## Demo Segments

### Segment 1 — Opening (0:00 - 1:00)

**Action**: Open `http://localhost:3000`. Create a new project or select existing.

**Panels visible**: Project Selector (full screen)

**Talking points**:
- "Dataverse AI Forge uses 10 specialized AI agents to build Microsoft Dataverse solutions through natural language"
- "Each agent has a specific role — architect, analyst, builder, reviewer, tester, and more"
- "Let's build a feedback management system for Contoso"

---

### Segment 2 — First Message (1:00 - 2:00)

**Action**: Type: "I need a feedback management system for Contoso. Customers submit feedback with ratings and categories, and our team responds to each one."

**Panels visible**: Sidebar (collapsed) | Workspace (Dashboard) | Chat (open)

**Talking points**:
- "The Solution Architect receives the request and decides which specialist to involve"
- Point out the agent badge and thinking trace in the chat
- "Watch the diagnostics — every tool call is logged in real time"

---

### Segment 3 — BA Elicitation (2:00 - 3:00)

**Action**: Wait for BA questions, then answer: "Categories: Product, Service, Support, General. Rating 1-5. Track responses. Status: New, Under Review, Responded, Closed."

**Panels visible**: Sidebar | Workspace | Chat (BA bubble with purple dot)

**Talking points**:
- "The Business Analyst asks clarifying questions — just like a real requirements session"
- "Notice the agent icon changed to purple — that's the BA"
- Click Bug icon to show DiagPanel briefly

---

### Segment 4 — SRS Card (3:00 - 4:00)

**Action**: Wait for BA to generate the SRS card. Expand sections.

**Panels visible**: Sidebar | Workspace | Chat (SRS card visible)

**Talking points**:
- "The BA produced a structured requirement specification"
- "Expand the sections — entities, business rules, relationships, security"
- "This is generated from a natural conversation, not a form"

---

### Segment 5 — Schema Review (4:00 - 5:00)

**Action**: Approve the requirements. Watch SA route to Reviewer.

**Panels visible**: Sidebar | Workspace | Chat | DiagPanel (toggle on)

**Talking points**:
- "Before any changes are made, the Reviewer validates the schema design"
- "Score 92/100 — checks naming conventions, relationship design, field types"
- "This is the governance layer — every change goes through review"

---

### Segment 6 — Approval Card (5:00 - 6:00)

**Action**: Review the ApprovalCard for table creation. Click "Approve".

**Panels visible**: Sidebar | Workspace | Chat (ApprovalCard with orange risk pill)

**Talking points**:
- "Human-in-the-loop: the Data Model Builder shows exactly what it plans to create"
- "Risk level is orange because this creates a new table"
- "Expand 'Details' to see the exact OData payload"
- "Click Approve — or you could Edit or Reject"

---

### Segment 7 — Creation + Verification (6:00 - 7:30)

**Action**: Watch tables being created. Observe sidebar tree populating.

**Panels visible**: Sidebar (expanded, tree updating) | Workspace | Chat | DiagPanel

**Talking points**:
- "Watch the sidebar — tables appear as they're created in Dataverse"
- "The Tester automatically re-reads everything from the API to verify"
- "PASS on all fields, types, and relationships"
- Toggle to Activity Log view: "Every operation is tracked with duration and status"

---

### Segment 8 — Timeline View (7:30 - 8:30)

**Action**: Click "Timeline" in sidebar navigation.

**Panels visible**: Sidebar | Workspace (AgentTimeline) | Chat (minimize optional)

**Talking points**:
- "The timeline shows a Gantt chart of every agent's activity"
- "Color-coded by agent — blue for Architect, purple for BA, green for Builder"
- "Hover over bars to see tool names and durations"
- "Total session time shown in top right"

---

### Segment 9 — Token Panel (8:30 - 9:15)

**Action**: Click the token/cost pill in the sidebar footer.

**Panels visible**: Sidebar | Workspace | TokenPanel (overlay)

**Talking points**:
- "Per-agent breakdown: which agents used which models, how many tokens"
- "SA and BA use GPT-4o for complex reasoning, builders use GPT-4o-mini"
- "Total cost: under $0.50 for the entire solution"
- "Manual equivalent line: a consultant would charge ~$4,800 for the same work"

---

### Segment 10 — Theme + Wrap-up (9:15 - 10:00)

**Action**: Toggle to light theme via sidebar. Switch back to Overview.

**Panels visible**: Sidebar | Workspace (Dashboard) | Chat

**Talking points**:
- "Full light theme support — every component adapts"
- "Dashboard shows solution stats, component grid, recent activity"
- "The Documentor has already generated a data dictionary and ERD"
- "10 agents, full governance, under a dollar. Questions?"

---

## Panel State Reference

| Segment | Sidebar | Workspace | Chat | DiagPanel | TokenPanel |
|---------|---------|-----------|------|-----------|------------|
| 1 | hidden | ProjectSelector | hidden | hidden | hidden |
| 2 | collapsed | Dashboard | open | hidden | hidden |
| 3 | collapsed | Dashboard | open (BA) | hidden | hidden |
| 4 | collapsed | Dashboard | open (SRS) | hidden | hidden |
| 5 | collapsed | Dashboard | open | open | hidden |
| 6 | collapsed | Dashboard | open (Approval) | hidden | hidden |
| 7 | expanded | Dashboard | open | open | hidden |
| 8 | expanded | Timeline | optional | hidden | hidden |
| 9 | expanded | Timeline | optional | hidden | open |
| 10 | expanded | Dashboard | open | hidden | hidden |

---

## Fallback: Demo Mode

If the backend is unavailable or unstable during the demo:

1. Navigate to `http://localhost:3000?demo=true`
2. The full UI loads with pre-populated data from a completed "Contoso Feedback" session
3. All views work: Dashboard, ERD, Timeline, Activity Log, Table Detail
4. Chat shows 14 pre-recorded messages including SRS card and approved ApprovalCard
5. Token panel shows per-agent breakdown totaling ~$0.47
6. Theme toggle works normally
7. To exit demo mode, click "Switch Project" in sidebar or navigate to `/`
