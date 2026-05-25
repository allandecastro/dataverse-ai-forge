namespace DataverseForge.Application.Agents;

// v1 ships narrow templates rather than freeform XAML construction — anything
// outside the template set refuses with a maker-portal pointer. The agent's
// job is to map user intent onto a small palette of supported shapes (one BPF
// template, four Business Rule templates) and keep the blast radius narrow.
public static class AutomationBuilderPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("AutomationBuilder.md")
        + "\n\n" + CommonPromptFragments.ApprovalReceivedRule
        + "\n\n" + CommonPromptFragments.CheckBeforeCreate
        + "\n\n" + CommonPromptFragments.GuidsRule
        + "\n\n" + CommonPromptFragments.NeverClaimWork
        + "\n\n" + CommonPromptFragments.QuoteToolErrorsVerbatim
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + CommonPromptFragments.CallToolsDontEchoNames
        + "\n\n" + CommonPromptFragments.HandoffSilenceRule
        + "\n\n" + CommonPromptFragments.NoIterationRepeatRule
        + "\n\n" + CommonPromptFragments.ReadTailFirst
        + "\n\n" + CommonPromptFragments.ReasoningTrace
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
