namespace DataverseForge.Application.Agents;

public static class UIBuilderPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("UIBuilder.md")
        + "\n\n" + CommonPromptFragments.ChatFormattingRule
        + "\n\n" + CommonPromptFragments.QuoteToolErrorsVerbatim
        + "\n\n" + CommonPromptFragments.ApprovalReceivedRule
        + "\n\n" + CommonPromptFragments.CheckBeforeCreate
        + "\n\n" + CommonPromptFragments.GuidsRule
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + CommonPromptFragments.HandoffSilenceRule
        + "\n\n" + CommonPromptFragments.NoIterationRepeatRule
        + "\n\n" + CommonPromptFragments.ReasoningTrace
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
