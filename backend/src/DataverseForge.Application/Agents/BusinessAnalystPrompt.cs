namespace DataverseForge.Application.Agents;

/// <summary>
/// Thin wrapper around the embedded <c>BusinessAnalyst.md</c> prompt. Same
/// public API as before (<see cref="SystemPrompt"/>) — main body now lives in
/// real markdown for cleaner diffs and GitHub/Cursor previews.
/// </summary>
public static class BusinessAnalystPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("BusinessAnalyst.md")
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + CommonPromptFragments.HandoffSilenceRule
        + "\n\n" + CommonPromptFragments.NoIterationRepeatRule
        + "\n\n" + CommonPromptFragments.ReasoningTrace
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
