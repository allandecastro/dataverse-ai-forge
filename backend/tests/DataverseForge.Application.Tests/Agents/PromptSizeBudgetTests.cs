using DataverseForge.Application.Agents;
using Xunit.Abstractions;

namespace DataverseForge.Application.Tests.Agents;

/// <summary>
/// Budget test for agent system prompts. Each prompt has a character-count
/// budget set at roughly current + 10% headroom. The test fires when someone
/// crosses the budget — at which point the contributor must either trim
/// content (extract dupes into <c>CommonPromptFragments</c>, move stable
/// reference tables into embedded markdown, drop dead examples) or
/// consciously bump the budget and explain why in the PR.
///
/// Why this exists: as of 2026-05-25 the eight agent prompts total ~3,800
/// LOC / ~180 KB of text and have grown every commit. Without a budget,
/// growth is invisible — every "just add one more rule" looks small in isolation
/// and adds up to silent token-cost regressions on every chat turn.
///
/// Why characters, not LOC or tokens:
/// - LOC depends on editor wrap settings and obscures long lines.
/// - Tokens would be ideal but pulling in a tokenizer (tiktoken etc.) for
///   one test is overkill. Chars correlate well enough (~4 chars/token for
///   English prose) to catch meaningful growth.
///
/// To update a budget after a deliberate change:
/// 1. Run this test — the failure message prints the new size.
/// 2. Set the new budget to (new size) + 10%, rounded up to the next 500.
/// 3. Mention the budget bump (with the why) in the commit message.
/// </summary>
public class PromptSizeBudgetTests(ITestOutputHelper output)
{
    /// <summary>
    /// Per-agent character budgets. Numbers were set on 2026-05-25 at
    /// (then-current <c>SystemPrompt.Length</c>) + ~10%, rounded up to the
    /// next 500. Current actuals at that time totalled 270,050 chars across
    /// the 8 agents — that is a lot of text being shipped on every chat
    /// turn, and the recommended 3-step reduction plan (this test +
    /// CommonPromptFragments dedupe + embedded markdown for stable tables)
    /// should bring it down.
    /// </summary>
    public static IEnumerable<object[]> Prompts() => new[]
    {
        // name, prompt, budget    // actual + delta history
        new object[] { "AutomationBuilder", AutomationBuilderPrompt.SystemPrompt,  25_000 }, // 22,650 (2026-05-25 after .md refactor; was 23,847 with ═══ dividers)
        new object[] { "BusinessAnalyst",   BusinessAnalystPrompt.SystemPrompt,    12_500 }, // 11,007 (.md refactor; was 11,485) → 12,089 (added Project Knowledge Base) → ~11,600 (DocAgent refactor: KB management delegated)
        new object[] { "DataModelBuilder",  DataModelBuilderPrompt.SystemPrompt,   39_500 }, // 32,765 (.md restyle; was 35,707, 37,192)
        new object[] { "DataPopulation",    DataPopulationPrompt.SystemPrompt,     29_000 }, // 25,910 (.md refactor; was 26,801)
        new object[] { "DocumentaryAgent",  DocumentaryAgentPrompt.SystemPrompt,   11_000 }, // new agent (2026-08-14): ~9,500 chars
        new object[] { "SecurityBuilder",   SecurityBuilderPrompt.SystemPrompt,    31_000 }, // 27,786 (.md refactor; was 29,123)
        new object[] { "SolutionArchitect", SolutionArchitectPrompt.SystemPrompt,  64_000 }, // 57,903 (.md refactor; was 59,659) → ~57,000 (DocAgent refactor: KB section trimmed)
        new object[] { "Tester",            TesterPrompt.SystemPrompt,             29_500 }, // 26,576 (.md refactor; was 27,489)
        new object[] { "UIBuilder",         UIBuilderPrompt.SystemPrompt,          63_000 }, // 54,305 (.md restyle/de-dup; was 58,152)
    };

    [Theory]
    [MemberData(nameof(Prompts))]
    public void AgentPromptIsUnderItsBudget(string name, string prompt, int budgetChars)
    {
        // Print every run so the actual size is always visible in test output,
        // not just when the assertion fails. Useful for budget-tightening passes.
        output.WriteLine($"{name,-20} {prompt.Length,7} chars / {budgetChars,7} budget " +
                         $"({(double)prompt.Length / budgetChars:P0} used)");

        Assert.True(
            prompt.Length <= budgetChars,
            $"{name}.SystemPrompt is {prompt.Length} chars, over the {budgetChars}-char budget " +
            $"(+{prompt.Length - budgetChars}). " +
            $"Either trim — extract shared paragraphs into CommonPromptFragments, " +
            $"move stable reference tables into embedded .md, drop dead examples — " +
            $"or consciously bump the budget in PromptSizeBudgetTests.Prompts() " +
            $"and document the why in the commit message.");
    }

    [Fact]
    public void TotalPromptBudgetIsCappedAcrossAllAgents()
    {
        // Soft cap on the SUM of all agent prompts. Catches the failure mode
        // where each individual prompt stays under its budget but the team
        // adds a new agent every quarter and total prompt cost balloons.
        // Actual on 2026-05-25 was 270,050 chars across the 8 agents; budget
        // set at +10% rounded up to the next 5,000 to give a stable ceiling
        // without forcing immediate cleanup.
        const int TotalBudgetChars = 300_000;

        var total = Prompts().Sum(row => ((string)row[1]).Length);
        output.WriteLine($"TOTAL agent prompt budget: {total} chars / {TotalBudgetChars} ({(double)total / TotalBudgetChars:P0})");

        Assert.True(
            total <= TotalBudgetChars,
            $"Sum of all agent prompts is {total} chars, over the {TotalBudgetChars}-char " +
            $"global cap. Time to invest in the 3-step prompt-reduction plan " +
            $"(prompt-size budget test + CommonPromptFragments dedupe + " +
            $"embedded .md reference tables) before adding more.");
    }
}
