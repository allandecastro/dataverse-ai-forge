using DataverseForge.Application.Agents;

namespace DataverseForge.Application.Tests.Agents;

/// <summary>
/// Guards against silent prompt-fragment drift: every builder agent must include
/// the <see cref="CommonPromptFragments.BuilderEssentials"/> guardrails. This
/// caught UIBuilder (missing NeverClaimWork + CallToolsDontEchoNames) and
/// DataPopulation (missing NeverClaimWork + HandoffSilenceRule +
/// NoIterationRepeatRule) — exactly the bug classes those agents had hit.
/// </summary>
public class PromptCompositionTests
{
    public static TheoryData<string, string> BuilderPrompts() => new()
    {
        { "DataModelBuilder", DataModelBuilderPrompt.SystemPrompt },
        { "UIBuilder", UIBuilderPrompt.SystemPrompt },
        { "DataPopulation", DataPopulationPrompt.SystemPrompt },
        { "SecurityBuilder", SecurityBuilderPrompt.SystemPrompt },
        { "AutomationBuilder", AutomationBuilderPrompt.SystemPrompt },
    };

    [Theory]
    [MemberData(nameof(BuilderPrompts))]
    public void BuilderPrompt_IncludesEveryEssentialFragment(string agentName, string prompt)
    {
        foreach (var fragment in CommonPromptFragments.BuilderEssentials)
        {
            Assert.True(
                prompt.Contains(fragment, StringComparison.Ordinal),
                $"{agentName} prompt is missing a required builder-essential fragment. " +
                "Add it to the agent's prompt composition (or, if the omission is truly " +
                "intentional, remove it from CommonPromptFragments.BuilderEssentials with a reason).");
        }
    }
}
