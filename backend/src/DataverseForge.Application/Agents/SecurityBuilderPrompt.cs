namespace DataverseForge.Application.Agents;

// Universal rules (think-tag, GUIDs, never-claim-work) come from
// CommonPromptFragments. SB-specific phrasing (BU-required, privilege-name
// resolution, delete-safety, depth normalization) lives in the embedded
// SecurityBuilder.md.
public static class SecurityBuilderPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("SecurityBuilder.md")
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
