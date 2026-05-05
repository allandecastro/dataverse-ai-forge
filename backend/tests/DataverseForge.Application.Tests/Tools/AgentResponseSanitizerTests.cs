using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class AgentResponseSanitizerTests
{
    // ─── Repeated VERIFICATION REPORT trim ───
    // Real session 2026-05-05 had the Tester emit the same FAIL block 30+
    // times in one response. The sanitizer keeps the first occurrence and
    // truncates the rest with a footer the user can see.

    [Fact]
    public void TrimRepeatedVerificationReports_SingleReport_LeftUnchanged()
    {
        var input = """
            <think>Verifying chart.</think>
            VERIFICATION REPORT
            Verdict: PASS
            - Gift Type Pie Chart matches plan
            """;
        var output = AgentResponseSanitizer.TrimRepeatedVerificationReports(input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void TrimRepeatedVerificationReports_FivePastedCopies_KeepsFirstAndAddsFooter()
    {
        // Five identical reports — the second through fifth must be dropped.
        var report = """
            <think>Verification failed.</think>
            VERIFICATION REPORT
            Verdict: FAIL
            - Test data: count_records returned HTTP 400
            """;
        var input = string.Join("\n", Enumerable.Repeat(report, 5));

        var output = AgentResponseSanitizer.TrimRepeatedVerificationReports(input);

        // Only ONE "VERIFICATION REPORT" header should remain.
        var remainingHeaders = System.Text.RegularExpressions.Regex.Matches(
            output, @"VERIFICATION\s+REPORT", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.Single(remainingHeaders);
        // Truncation footer should be appended so the user knows what happened.
        Assert.Contains("Output was truncated", output, StringComparison.Ordinal);
        Assert.Contains("5 times", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TrimRepeatedVerificationReports_ConcatenatedWithoutNewlines_StillTrimmed()
    {
        // Real session 2026-05-05 (verify_app_against_plan FAIL loop):
        // Tester glued 12 reports together with no paragraph breaks between
        // them — "...resolution.VERIFICATION REPORT..." — so the previous
        // multiline `^` anchor missed every occurrence after the first. Now
        // we match the header anywhere via word-boundary.
        // Mirrors the real-session shape: each report's trailing
        // sentence ends with a period, the next "VERIFICATION REPORT"
        // header is glued directly to it with no newline.
        var input =
            "VERIFICATION REPORT\nVerdict: FAIL\n- Demo Hub: not found\n\n"
            + "Handing off to Solution Architect for investigation and resolution."
            + "VERIFICATION REPORT\nVerdict: FAIL\n- Demo Hub: still missing.\n"
            + "Handing off to Solution Architect."
            + "VERIFICATION REPORT\nVerdict: FAIL\n- Demo Hub: still missing.";

        var output = AgentResponseSanitizer.TrimRepeatedVerificationReports(input);

        var remainingHeaders = System.Text.RegularExpressions.Regex.Matches(
            output, @"VERIFICATION\s+REPORT", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.Single(remainingHeaders);
        Assert.Contains("Output was truncated", output, StringComparison.Ordinal);
        Assert.Contains("3 times", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TrimRepeatedVerificationReports_TwoCopies_KeepsFirstOnly()
    {
        var input = """
            VERIFICATION REPORT
            Verdict: PASS
            - First time

            VERIFICATION REPORT
            Verdict: PASS
            - Second time
            """;
        var output = AgentResponseSanitizer.TrimRepeatedVerificationReports(input);

        Assert.Contains("First time", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Second time", output, StringComparison.Ordinal);
    }

    // ─── JSON record dumps ───
    // "We don't care about JSON of the data agent" — direct user feedback.

    [Fact]
    public void StripJsonRecordsDumps_ArrayOfRecords_Removed()
    {
        var input = """
            Here is the array I'll create:

            ```json
            [
              {"eppc_giftname":"R1","statecode":0},
              {"eppc_giftname":"R2","statecode":1}
            ]
            ```

            Should I proceed?
            """;
        var output = AgentResponseSanitizer.StripJsonRecordsDumps(input);

        Assert.DoesNotContain("eppc_giftname", output, StringComparison.Ordinal);
        Assert.Contains("records JSON omitted", output, StringComparison.Ordinal);
        // Surrounding prose preserved.
        Assert.Contains("Here is the array", output, StringComparison.Ordinal);
        Assert.Contains("Should I proceed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StripJsonRecordsDumps_SingleObjectWorkedExample_LeftUnchanged()
    {
        // Worked-example single-object snippets aren't payload dumps —
        // they're explanatory. Don't strip.
        var input = """
            Here is what a record shape looks like:

            ```json
            {"eppc_giftname":"Acme Pen","eppc_value":12.50}
            ```

            Pass an array of these.
            """;
        var output = AgentResponseSanitizer.StripJsonRecordsDumps(input);

        Assert.Contains("eppc_giftname", output, StringComparison.Ordinal);
        Assert.DoesNotContain("records JSON omitted", output, StringComparison.Ordinal);
    }

    // ─── Bare-GUID list redaction ───
    // Real session: SA listed 5 contact GUIDs as `1. 362f3de3-…  2. 2104801a-…`
    // — GUIDs are internal references, not user-facing.

    [Fact]
    public void RedactBareGuidLists_NumberedListOf5Guids_Replaced()
    {
        var input = """
            I retrieved 5 contacts to use as recipients:

            1. 362f3de3-48d8-f011-8544-7ced8d108e4f
            2. 2104801a-0fac-f011-bbd2-7ced8d108e4f
            3. 42e943ad-e9b5-f011-bbd3-7ced8d108e4f
            4. cd4381fc-e9b5-f011-bbd3-7ced8d108e4f
            5. ca8b8167-00c7-f011-bbd3-7ced8d5e0c19

            Shall I proceed?
            """;
        var output = AgentResponseSanitizer.RedactBareGuidLists(input);

        Assert.DoesNotContain("362f3de3", output, StringComparison.Ordinal);
        Assert.DoesNotContain("ca8b8167", output, StringComparison.Ordinal);
        Assert.Contains("5 record IDs", output, StringComparison.Ordinal);
        Assert.Contains("Shall I proceed", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactBareGuidLists_OneGuid_LeftUnchanged()
    {
        // A single GUID isn't a "list" — could be a meaningful identifier
        // (e.g. dashboard ID) the agent legitimately needs to reference once.
        var input = "Created dashboard with ID 362f3de3-48d8-f011-8544-7ced8d108e4f.";
        var output = AgentResponseSanitizer.RedactBareGuidLists(input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void RedactBareGuidLists_NamedList_LeftUnchanged()
    {
        // Names + GUIDs is fine — that's actual content. We only strip
        // bare GUIDs without context.
        var input = """
            Contacts:
            1. Allan De Castro (362f3de3-48d8-f011-8544-7ced8d108e4f)
            2. Nancie Calder (2104801a-0fac-f011-bbd2-7ced8d108e4f)
            """;
        var output = AgentResponseSanitizer.RedactBareGuidLists(input);
        Assert.Equal(input, output);
    }

    // ─── End-to-end Clean() ───

    [Fact]
    public void Clean_DataPopulationAgent_StripsJsonAndGuids()
    {
        var input = """
            I retrieved 3 contacts:

            1. 362f3de3-48d8-f011-8544-7ced8d108e4f
            2. 2104801a-0fac-f011-bbd2-7ced8d108e4f
            3. 42e943ad-e9b5-f011-bbd3-7ced8d108e4f

            Will create:

            ```json
            [{"eppc_giftname":"R1"},{"eppc_giftname":"R2"}]
            ```

            Approve?
            """;
        var output = AgentResponseSanitizer.Clean(input, "DataPopulation");

        Assert.DoesNotContain("362f3de3", output, StringComparison.Ordinal);
        Assert.DoesNotContain("eppc_giftname", output, StringComparison.Ordinal);
        Assert.Contains("Approve", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Clean_NonDataPopulationAgent_KeepsJsonButStripsGuids()
    {
        // SA isn't expected to dump JSON record arrays, so we don't strip
        // its code blocks (could be legitimate sample XML, etc.). But the
        // GUID-list pass applies to every agent.
        var input = """
            Found:
            1. 362f3de3-48d8-f011-8544-7ced8d108e4f
            2. 2104801a-0fac-f011-bbd2-7ced8d108e4f

            Sample shape:
            ```json
            [{"a":1}]
            ```
            """;
        var output = AgentResponseSanitizer.Clean(input, "SolutionArchitect");

        Assert.DoesNotContain("362f3de3", output, StringComparison.Ordinal);
        Assert.Contains("```json", output, StringComparison.Ordinal); // not stripped for SA
    }

    [Fact]
    public void Clean_EmptyInput_ReturnedUnchanged()
    {
        Assert.Equal("", AgentResponseSanitizer.Clean("", "DataPopulation"));
        Assert.Equal("   ", AgentResponseSanitizer.Clean("   ", "DataPopulation"));
    }

    // ─── Internal-apology stripping ───
    // Real session 2026-05-05 17:41: SA replied with
    //   "I realize I accidentally tried to hand off twice this turn, which
    //    is not allowed. I'll prepare the 100% stacked chart… I made a
    //    technical mistake trying to hand off twice in the same turn."
    // The user shouldn't have to learn what a per-turn handoff guard is.

    [Fact]
    public void StripInternalApologies_TheRealLeakedSentence_Removed()
    {
        var input = "Done — Gifts by Status (100% Stacked) chart created and verified. "
            + "I realize I accidentally tried to hand off twice this turn, which is not allowed. "
            + "Want me to do anything else?";
        var output = AgentResponseSanitizer.StripInternalApologies(input);
        Assert.DoesNotContain("accidentally", output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hand off twice", output, StringComparison.OrdinalIgnoreCase);
        // The other sentences must survive.
        Assert.Contains("Done — Gifts by Status", output, StringComparison.Ordinal);
        Assert.Contains("Want me to do anything else", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StripInternalApologies_TechnicalMistakePhrase_Removed()
    {
        var input = "I made a technical mistake trying to hand off twice in the same turn. "
            + "The chart was created successfully.";
        var output = AgentResponseSanitizer.StripInternalApologies(input);
        Assert.DoesNotContain("technical mistake", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chart was created", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StripInternalApologies_OneHandoffPerTurnQuote_Removed()
    {
        var input = "Note that one handoff per turn is the rule here. Anyway, the work is done.";
        var output = AgentResponseSanitizer.StripInternalApologies(input);
        Assert.DoesNotContain("one handoff per turn", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("the work is done", output, StringComparison.Ordinal);
    }

    [Fact]
    public void StripInternalApologies_LegitimateSentence_LeftUnchanged()
    {
        // "Mistake" alone isn't a leak — only paired with "technical".
        var input = "If I made a mistake, please tell me and I'll fix it.";
        var output = AgentResponseSanitizer.StripInternalApologies(input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void StripInternalApologies_ResultEmptiesEntirely_FallsBackToOriginal()
    {
        // If the only content was an apology, we'd otherwise return ""
        // and the user gets nothing — keep the original text in that case
        // so the user at least sees something. The agent's next turn
        // should produce a better reply.
        var input = "I realize I accidentally tried to hand off twice this turn.";
        var output = AgentResponseSanitizer.StripInternalApologies(input);
        Assert.Equal(input, output);
    }

    [Fact]
    public void Clean_StripsInternalApologyAlongsideOtherPasses()
    {
        var input = "Done — chart built and verified. "
            + "I made a technical mistake trying to hand off twice in the same turn. "
            + "Want me to add it to a dashboard?";
        var output = AgentResponseSanitizer.Clean(input, "SolutionArchitect");
        Assert.DoesNotContain("technical mistake", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Done — chart built", output, StringComparison.Ordinal);
        Assert.Contains("dashboard?", output, StringComparison.Ordinal);
    }
}
