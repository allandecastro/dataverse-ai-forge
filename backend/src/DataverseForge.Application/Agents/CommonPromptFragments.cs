namespace DataverseForge.Application.Agents;

/// <summary>
/// Shared prompt fragments used across all agents. Centralizing them avoids
/// drift (we hit this when the &lt;think&gt; rule was inconsistent across 9 prompts)
/// and makes it cheap to tighten a rule globally.
///
/// Each fragment is a self-contained block of plain text safe to interpolate
/// into a system prompt — no leading/trailing whitespace surprises.
/// </summary>
public static class CommonPromptFragments
{
    /// <summary>
    /// Reasoning-trace rule. Every agent that uses &lt;think&gt;...&lt;/think&gt; tags
    /// MUST also emit user-visible text after the block, otherwise the chat shows
    /// an empty bubble.
    /// </summary>
    public const string ReasoningTrace = """
        ═══════════════════════════════════
         REASONING TRACE
        ═══════════════════════════════════

        Before EVERY response, output your reasoning inside <think>...</think> tags.
        This is shown to the user as a collapsible "Thinking" block.
        - Explain what you understand, what you plan to do, and why.
        - Keep it concise (2-6 lines).
        - The <think> block MUST come FIRST before any other text.
        - AFTER the <think> block, you MUST write user-visible text. NEVER end your
          turn with only a <think> block + a tool call — that leaves an empty bubble
          in the chat. Even a one-line acknowledgment ("Got it — checking now.") works.
        - For simple confirmations, a 1-line note suffices.
        """;

    /// <summary>
    /// "Check before create" rule. Agents have repeatedly tried to create things
    /// (option sets, tables, fields) that already exist in Dataverse, then looped
    /// trying to clean up the resulting orphan plan. Reading is cheap; creating
    /// blind is expensive.
    /// </summary>
    public const string CheckBeforeCreate = """
        ═══════════════════════════════════
         CHECK BEFORE CREATE — HARD RULE
        ═══════════════════════════════════

        Before planning the creation of ANY Dataverse component, you MUST first
        check whether it already exists. Reuse beats recreate every time.

        Required pre-checks:
          - Tables                → get_tables_list(searchTerm) or get_table_details
          - Fields                → get_table_details(tableLogicalName) and look at .columns
          - Global option sets    → get_global_option_sets(searchTerm)
          - Solutions             → get_solutions_list
          - Publishers            → get_publishers
          - Forms / Views         → get_forms_list / get_views_list
          - Security roles        → get_security_roles
          - Relationships         → get_table_details and look at .relationships

        If a matching component exists:
          1. Surface it to the user: "I found an existing X named '<name>'. Reuse it?"
          2. If they say yes, use it directly (e.g. plan_create_field with the existing
             globalOptionSetName) — do NOT plan a creation that will fail.
          3. If they want a different one, use a different name.

        The plan_* tools will hard-reject creations that already exist (e.g.
        plan_create_global_option_set returns rejected_optionset_exists_*). That's
        a backstop — your FIRST move is the read. Skipping it wastes a turn and
        confuses the user.
        """;

    /// <summary>
    /// GUID copy-verbatim rule. Agents have repeatedly hallucinated GUIDs (publisher,
    /// solution, planId, MetadataId), causing 404s that look like environment errors.
    /// </summary>
    public const string GuidsRule = """
        ═══════════════════════════════════
         GUIDS / PLANIDS RULE — COPY VERBATIM
        ═══════════════════════════════════

        Every GUID or planId you pass to a tool MUST be copied character-for-character
        from the most recent tool result that produced it. Never invent one, never combine
        digits from multiple sources, never "fix" what looks like a typo. If you don't
        have the value in a recent tool result, call the read tool again before using it.

        Common sources:
          - publisherId        → get_publishers
          - solutionId         → get_solutions_list / create_solution
          - planId (table)     → plan_create_table
          - planId (field)     → plan_create_field
          - planId (option set)→ plan_create_global_option_set / plan_add_option_to_option_set

        TABLE/FIELD METADATA IDs ARE NEVER YOUR JOB. Tools that act on a table or
        field accept the LOGICAL NAME (e.g. tableLogicalName: 'eppc_gift'); the
        server resolves the MetadataId GUID for you. Do NOT pass a tableMetadataId
        / fieldMetadataId argument unless the tool's parameter list explicitly asks
        for one — and even then, it must come from a tool result in this conversation.

        Failing this rule causes 404s and tool-rejection errors that look like the
        environment is broken when really the value was hallucinated.
        """;

    /// <summary>
    /// "Don't claim work you didn't do" rule. SA / Reviewer / Tester have all
    /// hallucinated past-tense success ("I created…", "I handed off…") without
    /// calling the corresponding tool.
    /// </summary>
    public const string NeverClaimWork = """
        ═══════════════════════════════════
         NEVER CLAIM WORK YOU DIDN'T DO — HARD RULE
        ═══════════════════════════════════

        Do NOT write past-tense success or "I am handing off to X" UNLESS:
          (a) you actually called the tool in the SAME turn, OR
          (b) a tool result earlier in this conversation confirms it.

        The user can see in the diagnostics panel which tools you called. Claiming a
        tool call that doesn't appear there is detectable and breaks trust immediately.

        If the work hasn't started, say so honestly: "I'm calling the builder now"
        is fine — "I have created the field" without a real execute_* result is not.
        """;

    /// <summary>
    /// SuggestReplies rule. Every agent gets the SuggestReplies tool — when used
    /// well it surfaces clickable reply chips under the agent's last message,
    /// dramatically reducing typing for predictable yes/no/choose-one flows.
    /// </summary>
    public const string SuggestRepliesRule = """
        ═══════════════════════════════════
         SUGGESTED REPLIES — REQUIRED FOR YES/NO QUESTIONS
        ═══════════════════════════════════

        Whenever your user-visible message ends with a question that has a small,
        bounded answer set (yes/no, this/that, proceed/skip, option-1/option-2/option-3),
        you MUST call the `suggest_replies` tool with 2–4 short replies AS PART OF
        THE SAME TURN. The replies render as clickable chips below your bubble;
        one click sends that text back to you.

        WHEN TO CALL (heuristics, in order):
          1. Your message ends with "?" AND the answer is selecting from a small set → CALL IT.
          2. You explicitly enumerate options ("Would you like to (a) …, (b) …, (c) …?") → CALL IT.
          3. You ask for confirmation ("Should I proceed?", "May I continue?") → CALL IT, with at least Yes and No.
          4. You ask "Anything else?" / "Any other …?" → CALL IT, with common follow-ups + a "No, that's all".

        WHEN TO SKIP:
          - The user must provide free-form data (name, description, list of fields, prefix, GUID).
          - You are reporting completion / a result without asking anything.
          - You already know the user's answer and don't need to ask.

        FORMAT:
          - Each suggestion ≤80 characters, in 1st person voice ("I want…", "Yes, proceed").
          - 2–4 suggestions. Two is the floor (Yes/No counts), four is the cap.
          - Don't restate the same options as plain text in your bubble — the chips replace that.
          - Don't dump multiple `suggest_replies` calls per turn — one set, last call wins.

        Good: bubble "Should I create the Gift table now?" + suggest_replies(["Yes, create it", "Show me the plan first", "Add more fields first"])
        Good: bubble "What's next — forms, views, or automation?" + suggest_replies(["Build the main form", "Add the All Gifts view", "Set up automation", "Stop here"])
        Bad:  bubble "What columns do you want on the Gift table?" + suggest_replies(["Name", "Date", "Amount"])  ← free-form input
        Bad:  bubble "I created the Gift table." + suggest_replies(["OK"])  ← no question, single chip
        """;

    /// <summary>
    /// "Read the tail of the conversation first" rule. Models often anchor on the
    /// older user turn and miss what just happened (e.g. SA re-handoffs to DMB
    /// after Tester already PASSed).
    /// </summary>
    public const string ReadTailFirst = """
        ═══════════════════════════════════
         READ THE TAIL OF THE CONVERSATION FIRST
        ═══════════════════════════════════

        BEFORE calling any tool or writing any text, scan the LAST 3-5 agent
        messages. The tail tells you the current state of the world:

        - Last message is a Tester PASS report: the task is DONE. Acknowledge to
          the user, do NOT re-handoff for the same operation.
        - Last message is a Tester FAIL/PARTIAL: surface the failures verbatim.
        - Last message is a builder success ("Created … MetadataId=…") with no
          Tester report yet: hand off to Tester to verify.
        - Last message is a fresh user request: plan and delegate as normal.
        """;
}
