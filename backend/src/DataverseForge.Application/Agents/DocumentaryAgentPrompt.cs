namespace DataverseForge.Application.Agents;

public static class DocumentaryAgentPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("DocumentaryAgent.md")
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + CommonPromptFragments.HandoffSilenceRule
        + "\n\n" + CommonPromptFragments.NoIterationRepeatRule
        + "\n\n" + CommonPromptFragments.ReasoningTrace
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
