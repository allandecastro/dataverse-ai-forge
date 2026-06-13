using System.Reflection;
using DataverseForge.Application.Agents;

namespace DataverseForge.Application.Tests.Agents;

/// <summary>
/// Guardrail against unbounded agent-prompt growth (health-check TODO #6).
///
/// Every agent system prompt ships to the model on EVERY turn, so prompt size
/// is a direct, recurring token cost. Prompts have a habit of creeping upward as
/// edge cases get documented inline. These tests don't dictate a "correct" size
/// — they just fail when a prompt crosses a generous ceiling, forcing a
/// conscious decision: extract shared text into <see cref="CommonPromptFragments"/>,
/// move stable reference tables to runtime-injected state, or (deliberately)
/// raise the budget here with a note explaining why.
/// </summary>
public class PromptBudgetTests
{
    private const string ResourceNamespace = "DataverseForge.Application.Agents.Prompts";

    // Headroom over today's largest prompt (~47 KB / ~907 lines as of 2026-06).
    // Crossing these means "stop and refactor", not "bump the number".
    private const int MaxCharsPerPrompt = 60_000;
    private const int MaxLinesPerPrompt = 1_100;

    // Catches death-by-a-thousand-cuts across all prompts (today ~172 KB total).
    private const int MaxTotalChars = 220_000;

    private static readonly Assembly PromptAssembly = typeof(EmbeddedPrompts).Assembly;

    private static IEnumerable<string> PromptResourceNames() => PromptAssembly
        .GetManifestResourceNames()
        .Where(n => n.StartsWith(ResourceNamespace, StringComparison.Ordinal)
                    && n.EndsWith(".md", StringComparison.Ordinal));

    private static string ReadResource(string resourceName)
    {
        using var stream = PromptAssembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static TheoryData<string> AllPrompts()
    {
        var data = new TheoryData<string>();
        foreach (var name in PromptResourceNames()) data.Add(name);
        return data;
    }

    [Fact]
    public void AtLeastOnePromptIsEmbedded()
    {
        // If the csproj glob breaks, every per-prompt theory would silently pass
        // with zero cases. This is the canary that the resources exist at all.
        Assert.NotEmpty(PromptResourceNames());
    }

    [Theory]
    [MemberData(nameof(AllPrompts))]
    public void Prompt_IsWithinPerFileBudget(string resourceName)
    {
        var content = ReadResource(resourceName);
        var chars = content.Length;
        var lines = content.Count(c => c == '\n') + 1;

        Assert.True(chars <= MaxCharsPerPrompt,
            $"{resourceName} is {chars} chars (budget {MaxCharsPerPrompt}). " +
            "Prompts ship every turn — extract shared text into CommonPromptFragments, " +
            "move stable reference tables to runtime-injected ProjectState, or raise the " +
            "budget here with a justifying comment.");

        Assert.True(lines <= MaxLinesPerPrompt,
            $"{resourceName} is {lines} lines (budget {MaxLinesPerPrompt}). See guidance above.");
    }

    [Fact]
    public void AllPrompts_StayWithinTotalBudget()
    {
        var total = PromptResourceNames().Sum(n => ReadResource(n).Length);
        Assert.True(total <= MaxTotalChars,
            $"All agent prompts total {total} chars (budget {MaxTotalChars}). The aggregate " +
            "prompt surface is creeping up — consolidate shared sections before adding more.");
    }
}
