namespace DataverseForge.Application.Agents;

// Refactor note: in the previous shape, NeverClaimWork and ReadTailFirst
// were interleaved in the MIDDLE of the main prompt (around the
// "Tester reports back" section). They are now at the end of the
// composition with the other universal rules — both positions work
// equivalently for universal reminders, and the flat composition makes
// the prompt structure easier to read at a glance.
public static class SolutionArchitectPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("SolutionArchitect.md")
        + "\n\n" + CommonPromptFragments.NeverClaimWork
        + "\n\n" + CommonPromptFragments.ReadTailFirst
        + "\n\n" + CommonPromptFragments.CheckBeforeCreate
        + "\n\n" + CommonPromptFragments.GuidsRule
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + CommonPromptFragments.ChatFormattingRule
        + "\n\n" + CommonPromptFragments.CallToolsDontEchoNames
        + "\n\n" + CommonPromptFragments.HandoffSilenceRule
        + "\n\n" + CommonPromptFragments.NoIterationRepeatRule
        + "\n\n" + CommonPromptFragments.ReasoningTrace
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
