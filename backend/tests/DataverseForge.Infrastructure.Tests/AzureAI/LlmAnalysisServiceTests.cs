using DataverseForge.Application.Rag;
using DataverseForge.Infrastructure.AzureAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Infrastructure.Tests.AzureAI;

public class LlmAnalysisServiceTests
{
    private static LlmAnalysisService BuildService(string chatResponse)
    {
        var chatClientMock = new Mock<IChatClient>();
        chatClientMock.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, chatResponse)]));
        return new LlmAnalysisService(chatClientMock.Object, NullLogger<LlmAnalysisService>.Instance);
    }

    // ─── ExtractRequirements ─────────────────────────────────────────────────

    [Fact]
    public async Task ExtractRequirementsAsync_NumberedList_ReturnsStrippedLines()
    {
        var svc = BuildService("1. The system must validate amounts\n2. Invoices must have a unique number\n3. Users must be authenticated");

        var requirements = await svc.ExtractRequirementsAsync("Some spec text");

        Assert.Equal(3, requirements.Count);
        Assert.Equal("The system must validate amounts", requirements[0]);
        Assert.Equal("Invoices must have a unique number", requirements[1]);
        Assert.Equal("Users must be authenticated", requirements[2]);
    }

    [Fact]
    public async Task ExtractRequirementsAsync_FiltersShortLines_ExcludesNoise()
    {
        var svc = BuildService("1. Valid requirement that is long enough\n2. Ok\n3. Another valid requirement here");

        var requirements = await svc.ExtractRequirementsAsync("Some spec text");

        // "Ok" is < 10 chars and should be filtered
        Assert.Equal(2, requirements.Count);
        Assert.DoesNotContain("Ok", requirements);
    }

    // ─── CheckCoverage ───────────────────────────────────────────────────────

    [Fact]
    public async Task CheckCoverageAsync_CoveredKeyword_ReturnsCoveredStatus()
    {
        var svc = BuildService("COVERED The work item WI-42 fully addresses this requirement.");

        var verdict = await svc.CheckCoverageAsync("The system must validate amounts", ["WI-42: validates positive amounts"]);

        Assert.Equal(CoverageStatus.Covered, verdict.Status);
        Assert.Contains("WI-42", verdict.Explanation);
    }

    [Fact]
    public async Task CheckCoverageAsync_PartialKeyword_ReturnsPartiallyCoversStatus()
    {
        var svc = BuildService("PARTIAL WI-42 mentions amounts but not the zero-amount edge case.");

        var verdict = await svc.CheckCoverageAsync("Amounts must be positive and non-zero", ["WI-42: validates amounts"]);

        Assert.Equal(CoverageStatus.PartiallyCovers, verdict.Status);
    }

    [Fact]
    public async Task CheckCoverageAsync_NotCoveredKeyword_ReturnsNotCoveredStatus()
    {
        var svc = BuildService("NOT_COVERED No work item in the backlog addresses invoice uniqueness.");

        var verdict = await svc.CheckCoverageAsync("Invoices must have unique numbers", ["WI-1: create invoice form"]);

        Assert.Equal(CoverageStatus.NotCovered, verdict.Status);
    }

    [Fact]
    public async Task CheckCoverageAsync_NoCandidates_ReturnsNotCoveredWithoutCallingLlm()
    {
        // No LLM call expected — check happens before calling the model
        var chatClientMock = new Mock<IChatClient>();
        var svc = new LlmAnalysisService(chatClientMock.Object, NullLogger<LlmAnalysisService>.Instance);

        var verdict = await svc.CheckCoverageAsync("Some requirement", []);

        Assert.Equal(CoverageStatus.NotCovered, verdict.Status);
        chatClientMock.Verify(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── CheckConflict ───────────────────────────────────────────────────────

    [Fact]
    public async Task CheckConflictAsync_ContradictionKeyword_ReturnsContradictionRelation()
    {
        var svc = BuildService("CONTRADICTION Spec says strictly positive; export allows zero.");

        var verdict = await svc.CheckConflictAsync("Amount must be strictly positive", "Amount can be zero for free invoices");

        Assert.Equal(ConflictRelation.Contradiction, verdict.Relation);
        Assert.Contains("strictly positive", verdict.Explanation);
    }

    [Fact]
    public async Task CheckConflictAsync_ComplementKeyword_ReturnsComplementRelation()
    {
        var svc = BuildService("COMPLEMENT Both passages address validation but from different angles.");

        var verdict = await svc.CheckConflictAsync("Amounts must be validated", "Validation rules apply to invoice totals");

        Assert.Equal(ConflictRelation.Complement, verdict.Relation);
    }

    [Fact]
    public async Task CheckConflictAsync_UnrelatedKeyword_ReturnsUnrelatedRelation()
    {
        var svc = BuildService("UNRELATED The passages discuss different unrelated features.");

        var verdict = await svc.CheckConflictAsync("User login flow", "Invoice PDF generation");

        Assert.Equal(ConflictRelation.Unrelated, verdict.Relation);
    }

    [Fact]
    public async Task CheckConflictAsync_UnknownKeyword_FallsBackToUnrelated()
    {
        var svc = BuildService("MAYBE the model is unsure about this one.");

        var verdict = await svc.CheckConflictAsync("PassageA", "PassageB");

        Assert.Equal(ConflictRelation.Unrelated, verdict.Relation);
    }

    // ─── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractRequirementsAsync_ParenthesisNumbering_StripsPrefix()
    {
        // Some models use "1) " instead of "1. "
        var svc = BuildService("1) Validate amounts on submit\n2) Amounts must be positive integers");

        var requirements = await svc.ExtractRequirementsAsync("Some spec text");

        Assert.Equal(2, requirements.Count);
        Assert.Equal("Validate amounts on submit", requirements[0]);
        Assert.Equal("Amounts must be positive integers", requirements[1]);
    }

    [Fact]
    public async Task ExtractRequirementsAsync_EmptyResponse_ReturnsEmpty()
    {
        var svc = BuildService(string.Empty);

        var requirements = await svc.ExtractRequirementsAsync("Some spec text");

        Assert.Empty(requirements);
    }

    [Fact]
    public async Task CheckCoverageAsync_MultiLineExplanation_FullExplanationPreserved()
    {
        // Explanation spans two lines — the tool should capture it all
        var svc = BuildService("COVERED WI-42 addresses this requirement.\nIt also covers the edge case for zero.");

        var verdict = await svc.CheckCoverageAsync("Amount must be non-negative", ["WI-42: handles amounts"]);

        Assert.Equal(CoverageStatus.Covered, verdict.Status);
        Assert.Contains("WI-42", verdict.Explanation);
    }

    [Fact]
    public async Task CheckConflictAsync_ContradictionFirstWordNewline_ParsedCorrectly()
    {
        // Some models put the keyword on its own line
        var svc = BuildService("CONTRADICTION\nSpec disallows zero; export allows it.");

        var verdict = await svc.CheckConflictAsync("Amount must be positive", "Amount can be zero");

        Assert.Equal(ConflictRelation.Contradiction, verdict.Relation);
    }
}
