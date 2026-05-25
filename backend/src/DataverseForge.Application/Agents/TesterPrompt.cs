namespace DataverseForge.Application.Agents;

// REASONING TRACE for the Tester is intentionally inline (in Tester.md), NOT
// pulled from CommonPromptFragments.ReasoningTrace — the Tester's variant has
// stricter rules about the VERIFICATION REPORT shape that the generic
// fragment doesn't cover.
public static class TesterPrompt
{
    public static readonly string SystemPrompt =
        EmbeddedPrompts.Load("Tester.md")
        + "\n\n" + CommonPromptFragments.GuidsRule
        + "\n\n" + CommonPromptFragments.SuggestRepliesRule
        + "\n\n" + EmbeddedPrompts.ProjectStateTrailer;
}
