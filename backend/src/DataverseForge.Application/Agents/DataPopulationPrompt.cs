namespace DataverseForge.Application.Agents;

public static class DataPopulationPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("DataPopulation.md")
        + "\n\n" + CommonPromptFragments.ApprovalReceivedRule
        + "\n\n" + CommonPromptFragments.QuoteToolErrorsVerbatim
        + "\n\n" + CommonPromptFragments.CheckBeforeCreate
        + "\n\n" + CommonPromptFragments.GuidsRule
        + "\n\n" + CommonPromptFragments.NeverClaimWork
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + CommonPromptFragments.ChatFormattingRule
        + "\n\n" + CommonPromptFragments.CallToolsDontEchoNames
        + "\n\n" + CommonPromptFragments.HandoffSilenceRule
        + "\n\n" + CommonPromptFragments.NoIterationRepeatRule
        + "\n\n" + CommonPromptFragments.ReasoningTrace
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
