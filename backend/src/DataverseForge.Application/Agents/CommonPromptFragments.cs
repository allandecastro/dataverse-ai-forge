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
    /// "Don't claim work you didn't do" rule. SA / Tester have hallucinated
    /// past-tense success ("I created…", "I handed off…") without calling the
    /// corresponding tool.
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

        ⚠️ HARD RULE — TOOL CALL, NOT TEXT MARKUP ⚠️
        `suggest_replies` is a function tool. You CALL it, you do NOT type its name
        in the bubble. Past failure (do NOT repeat):

           Wrong (the model literally typed this in the message body):
             "[suggest_replies]
              - Yes, add the status reasons
              - No, do not add them"

           That string is just text — the frontend has no parser for it, no chips
           render, the user has to type the answer. Useless.

           Right: produce a normal bubble ending in the question, then issue a
           tool call to `suggest_replies` with `suggestions: ["Yes, add ...", "No, do not add ..."]`.
           No `[suggest_replies]` token, no inline list of bullets.

        WHEN TO CALL (heuristics, in order):
          1. Your message ends with "?" AND the answer is selecting from a small set → CALL IT.
          2. You explicitly enumerate options ("Would you like to (a) …, (b) …, (c) …?") → CALL IT.
          3. You ask for confirmation ("Should I proceed?", "May I continue?") → CALL IT, with at least Yes and No.
          4. You ask "Anything else?" / "Any other …?" → CALL IT, with common follow-ups + a "No, that's all".

        WHEN TO SKIP:
          - The user must provide free-form data (name, description, list of fields, prefix, GUID).
          - You are reporting completion / a result without asking anything.
          - You already know the user's answer and don't need to ask.
          - You ALREADY called `request_approval` or `request_batch_approval`
            this turn. The approval card IS the user response surface — adding
            chips on top is duplicate noise. Approval > suggested replies.

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
    /// Handoff hygiene — when you call handoff_to_*, your text bubble is the LAST
    /// thing the user sees from YOU this turn. The next agent takes over the
    /// next bubble. Asking "Anything else?" creates a confusing UX where the
    /// user is mid-sentence-typing while the next agent is already responding.
    /// </summary>
    public const string HandoffSilenceRule = """
        ═══════════════════════════════════
         HANDOFF HYGIENE — DO NOT ASK FOLLOW-UPS
        ═══════════════════════════════════

        When your turn includes a handoff_to_* call, the bubble accompanying
        that handoff is the LAST thing YOU say this turn. The next agent
        takes over the next bubble immediately and the user sees the agent
        switch at once. So:

        ❌ Do NOT ask "Anything else?" / "Want me to adjust X too?" / "Other
           questions?" in the same bubble as a handoff. The user can't
           respond to YOU when the next agent is already typing.

        ❌ Do NOT call `suggest_replies` on a handoff bubble. The chips would
           render under the wrong agent's response and confuse routing.

        ✅ Do state ONLY: what you're handing off, why, and what you expect
           back. Two short sentences max. Then stop.

        Wrong (real session):
            "I'm handing off to UI Builder to update the Test Main Form …
             Is there anything else you want to include in this form update?"

        Right:
            "Handing off to UI Builder to update the Test Main Form's header
             with Gift Number (read-only). I'll report back once it's done."
        """;

    /// <summary>
    /// "Don't repeat yourself across tool-call iterations" — the streaming runtime
    /// concatenates all of an agent's text emissions in a single turn into one
    /// bubble. When the model emits text → tool call → text → tool call → text,
    /// the user sees three paragraphs that often re-state the same conclusion
    /// in different words. Tell the model to advance, not restart.
    /// </summary>
    public const string NoIterationRepeatRule = """
        ═══════════════════════════════════
         DO NOT REPEAT YOURSELF ACROSS TOOL CALLS
        ═══════════════════════════════════

        A single turn from you can contain MULTIPLE chunks of text separated by
        tool calls (e.g. "I'll check…" → tool call → "Found X. Now planning…"
        → tool call → "Plan ready."). The user sees ALL these chunks in ONE
        bubble. Each chunk MUST advance the response — do NOT re-state the
        previous chunk's conclusion in slightly different words.

        Wrong (real session — same paragraph rewritten 3 times in one bubble):
            "The Gift table currently does not have any alternate keys defined.
             Would you like me to proceed?
             The Gift table currently has no alternate keys defined.
             Would you like me to create an alternate key on the field now?
             The Gift table currently has no alternate keys defined, so the
             field does not have an alternate key yet. Please confirm…"

        Right:
            (after the lookup tool call) "No alternate keys exist on Gift yet.
             Want me to create one on Gift Number?"
            (NOTHING else — wait for user reply)

        Rules:
          - After a tool result, write the FINAL conclusion only. Skip the
            "I checked, here's what I found" recap if the conclusion already
            implies it.
          - If you already asked the user a question earlier in the turn, do
            NOT ask it again with different wording.
          - Stop when you've said enough. Don't pad with restatements while
            waiting for the model to "decide" what to say next.
        """;

    /// <summary>
    /// "Approval received → execute, don't re-ask" rule. Builders have re-prompted
    /// the user for approval AFTER receiving the approval-receipt message,
    /// causing duplicate plans and stale-pending-plan errors. This fragment
    /// makes the trigger explicit and bans the re-ask.
    /// </summary>
    public const string ApprovalReceivedRule = """
        ═══════════════════════════════════
         APPROVAL RECEIVED → EXECUTE, DO NOT RE-ASK
        ═══════════════════════════════════

        The user signals approval through one of three latest-message shapes:
          - "User approved." (the approval-card OK button)
          - "User approved all N operations." (batch approval)
          - "Yes" / "Yes, proceed" / "Approve" / "Go ahead" (free-text)

        When you see ANY of these as the latest user message AND you have a
        pending plan from the prior turn (you just called plan_*), your NEXT
        action MUST be the corresponding execute_* tool call against the
        planId you just generated. Do NOT:
          - Re-propose the plan ("The update plan is ready, awaiting your
            approval. Should I apply it now?") — they just answered YES.
          - Re-call request_approval — the approval just came in.
          - Call suggest_replies with another Yes/No set — same reason.

        Real-session failure (do NOT repeat):
            UIBuilder: "I have planned the update … Should I go ahead?"
            User    : "User approved."
            UIBuilder (WRONG): "The update plan is ready with a medium risk
                                  level, and awaiting your approval to proceed.
                                  Should I apply this update now?"
                                  → loop. User had to type "Yes" a SECOND time.
            UIBuilder (RIGHT): fire execute_update_form(planId) immediately.

        If you somehow lost track of the planId (rare), the agent panel /
        diagnostics shows the most recent plan_* tool result; re-read its
        return value rather than re-planning.
        """;

    /// <summary>
    /// "Quote tool errors verbatim" rule. Builders have repeatedly paraphrased
    /// rejection messages ("encountered an error", "this might be because…")
    /// instead of surfacing the actual REJECTED text the planner returned.
    /// That hides the fix from both the user and the next turn's reasoning,
    /// turning a one-shot fix into multi-turn looping.
    /// </summary>
    public const string QuoteToolErrorsVerbatim = """
        ═══════════════════════════════════
         QUOTE TOOL ERRORS VERBATIM — DO NOT PARAPHRASE
        ═══════════════════════════════════

        When a plan_* / execute_* tool returns warnings starting with "REJECTED:"
        OR an error string, your user-visible reply MUST include the FIRST
        rejection sentence verbatim inside a fenced code block. The user
        can't fix what they can't see, and you can't fix it next turn either
        if the actual reason has been compressed into "encountered an error".

        Concretely:
        - plan_create_chart returns Warnings = ["REJECTED: STANDARD-FIELD MISTAKE: 'eppc_statecode' → use 'statecode' …"]
          → Your reply quotes that line. THEN re-call plan_create_chart with
          the corrected field. Don't ask the user to choose another field —
          the planner already told you which one to use.
        - execute_create_chart returns "ERROR: 0x80040217 entity reference …"
          → Your reply quotes the error. THEN explain what changed and what
          to retry.

        Anti-example FROM A REAL SESSION (do NOT repeat):
            plan_create_chart returns warnings ["REJECTED: STANDARD-FIELD MISTAKE …"]
            UIB (WRONG): "I attempted to create a pie chart grouping open Gifts
                          by the Status (statecode) field, but it encountered
                          an error. Would you like to try grouping by another
                          field?"
                          → The user has no idea what the error was, you
                          can't fix it without re-attempting blindly.
            UIB (RIGHT): "The chart plan was rejected:
                          ```
                          REJECTED: STANDARD-FIELD MISTAKE: 'eppc_statecode' →
                          use 'statecode' (it's a standard field, never prefixed).
                          ```
                          Re-running with `statecode` now."
                          → Then call plan_create_chart again with statecode.
        """;

    /// <summary>
    /// "Call tools, don't echo their names". Repeated regression: a model emits
    /// the literal tool name (`handoff_to_solution_architect`) or a JS-style
    /// invocation (`functions.CountRecords({…})`) as a chat reply instead of
    /// triggering the actual tool call. The user sees a wall of pseudo-code
    /// and is blocked because no SSE / handoff event ever fires.
    /// </summary>
    /// <remarks>
    /// HISTORICAL NOTE: earlier versions of this fragment included literal
    /// snake_case function-call syntax in the anti-examples (e.g. the actual
    /// string for the SA handoff in code form). That backfired — the model
    /// over-indexed on the example and started emitting the very pattern
    /// we were warning against. The fragment now describes the wrong shape
    /// in plain English ONLY, with no code-style examples to copy.
    /// </remarks>
    public const string CallToolsDontEchoNames = """
        ═══════════════════════════════════
         INVOKE TOOLS — DO NOT DESCRIBE THEM IN TEXT
        ═══════════════════════════════════

        When you decide a tool needs to run, invoke it through the normal
        tool-call channel. Your user-visible chat reply is for natural-language
        narration — it should NEVER contain a function-call syntax (no
        parentheses-and-arguments shape, no JS/Python/shell-style invocations,
        no backtick code blocks describing a tool call).

        If you need to acknowledge a handoff, write one short sentence in
        plain English — for example: "Handing this to the Solution Architect,
        who handles data work." The framework records the actual handoff;
        you don't need to spell it out in code.

        If your reply contains a function name followed by parentheses, you
        are doing it wrong — DELETE that text and either invoke the tool
        OR describe the action in plain words.

        If the tool you want isn't in your toolbelt, don't invent a way to
        call it. Say so plainly: "I don't have a tool to do X — let me hand
        off to the agent that does."
        """;

    /// <summary>
    /// User-visible chat formatting rules. The chat surface is for humans —
    /// not internal IDs, not JSON payloads, not the raw form an API expects.
    /// Recurring complaints from real sessions:
    /// "we don't care about JSON of the data agent" (DataPop dumped a 5-record
    /// JSON array mid-turn) and SA listing 5 contact GUIDs as a numbered list
    /// to the user. The platform sanitizer absorbs the worst cases, but every
    /// agent should also self-police to keep replies clean.
    /// </summary>
    public const string ChatFormattingRule = """
        ═══════════════════════════════════
         CHAT FORMATTING — WRITE FOR A HUMAN
        ═══════════════════════════════════

        Your reply is shown to a human, not consumed by an API. Keep it tight:

        - NEVER paste raw JSON payloads, OData bodies, or argument-shaped
          objects into chat. The actual data is staged server-side under the
          planId; the user only needs the SUMMARY.
            Bad : "Here is the records array I'll create: [{\"eppc_giftname\":\"…\"}, …]"
            Good: "Planning 5 sample gift records (varied statuses, [TEST DATA] tagged)."

        - NEVER list GUIDs as their own line items. GUIDs are internal — if
          you fetched 5 contacts, refer to them by name, OR write one short
          sentence ("Pulled 5 contacts as lookup targets.") and let the user
          ask if they want detail.
            Bad : "1. 362f3de3-48d8-…  2. 2104801a-…  3. …"
            Good: "Pulled 5 contacts (Allan De Castro, Nancie Calder, Julien
                   Normand, …) to use as gift recipients."

        - Quote real Dataverse error messages verbatim inside a fenced code
          block (those ARE useful), but don't quote your own request payload.

        - Keep the reply concise. 2-6 lines is plenty for most turns.
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
