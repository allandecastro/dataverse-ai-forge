namespace DataverseForge.Application.Agents;

// Universal rules (think-tag, GUIDs, never-claim-work) come from
// CommonPromptFragments and are appended at the end of the composition.
public static class DataModelBuilderPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("DataModelBuilder.md")
        + "\n\n" + CommonPromptFragments.ApprovalReceivedRule
        + "\n\n" + CommonPromptFragments.CheckBeforeCreate
        + "\n\n" + CommonPromptFragments.GuidsRule
        + "\n\n" + CommonPromptFragments.NeverClaimWork
        + "\n\n" + CommonPromptFragments.QuoteToolErrorsVerbatim
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + CommonPromptFragments.CallToolsDontEchoNames
        + "\n\n" + CommonPromptFragments.HandoffSilenceRule
        + "\n\n" + CommonPromptFragments.NoIterationRepeatRule
        + "\n\n" + CommonPromptFragments.ReasoningTrace
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
