using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyFormAgainstPlanToolTests
{
    private const string PlanId = "create_form_test1";
    private static readonly Guid FormId = Guid.Parse("11111111-aaaa-1111-aaaa-111111111111");
    private const string PlannedPayload = """
        {
          "name": "Account Main Form",
          "type": 2,
          "formxml": "<form><tabs><tab name='general'><label /></tab></tabs></form>",
          "objecttypecode": "account"
        }
        """;

    private readonly Mock<IDataverseClient> _dataverse = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private VerifyFormAgainstPlanTool MakeTool() =>
        new(_dataverse.Object, _store.Object, _ctx.Object);

    private ProjectState SetupSession(string sessionId, bool seedExecuted = true)
    {
        var state = new ProjectState { ProjectId = sessionId };
        if (seedExecuted) state.ExecutedPlans[PlanId] = PlannedPayload;
        _ctx.Setup(c => c.SessionId).Returns(sessionId);
        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task VerifyForm_Matches_ReturnsPass()
    {
        SetupSession("form-1");
        _dataverse.Setup(c => c.GetFormDefinitionAsync(FormId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormDefinition(
                FormId,
                "Account Main Form",
                "Main",
                "<form><tabs><tab name='general'><label /></tab></tabs></form>"));

        var result = await MakeTool().VerifyFormAgainstPlanAsync(PlanId, FormId.ToString(), CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
        Assert.True(result.Exists);
        Assert.True(result.Matches);
    }

    [Fact]
    public async Task VerifyForm_NameMismatch_ReturnsFail()
    {
        SetupSession("form-2");
        _dataverse.Setup(c => c.GetFormDefinitionAsync(FormId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormDefinition(FormId, "Different Name", "Main", "<form><tabs><tab name='general'><label /></tab></tabs></form>"));

        var result = await MakeTool().VerifyFormAgainstPlanAsync(PlanId, FormId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "name");
    }

    [Fact]
    public async Task VerifyForm_FormXmlDiverges_ReturnsFailWithLengthHint()
    {
        SetupSession("form-3");
        _dataverse.Setup(c => c.GetFormDefinitionAsync(FormId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormDefinition(FormId, "Account Main Form", "Main", "<form><tabs><tab name='other'><label /></tab></tabs></form>"));

        var result = await MakeTool().VerifyFormAgainstPlanAsync(PlanId, FormId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "formxml");
    }

    [Fact]
    public async Task VerifyForm_FormXmlWhitespaceOnly_ReturnsPass()
    {
        SetupSession("form-4");
        _dataverse.Setup(c => c.GetFormDefinitionAsync(FormId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormDefinition(
                FormId,
                "Account Main Form",
                "Main",
                "<form>  <tabs>\n  <tab name='general'><label /></tab>\n  </tabs></form>"));

        var result = await MakeTool().VerifyFormAgainstPlanAsync(PlanId, FormId.ToString(), CancellationToken.None);

        // Whitespace-only diff between tags should collapse to PASS — Dataverse
        // sometimes pretty-prints what it echoes back; we don't want a noisy FAIL.
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifyForm_WrongPrefix_ReturnsFailWithSuggestion()
    {
        SetupSession("form-5");
        var result = await MakeTool().VerifyFormAgainstPlanAsync(
            "create_view_xyz", FormId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual!.Contains("verify_view_against_plan"));
    }

    [Fact]
    public async Task VerifyForm_RejectedPlan_ReturnsFailWithClearMessage()
    {
        SetupSession("form-6");
        var result = await MakeTool().VerifyFormAgainstPlanAsync(
            "rejected_form_invalid_abc", FormId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual!.Contains("REJECTED PLAN"));
    }

    [Fact]
    public async Task VerifyForm_PlanPendingButNotExecuted_HintsAtMissingExecute()
    {
        var state = new ProjectState { ProjectId = "form-7" };
        state.PendingPlans[PlanId] = PlannedPayload;
        _ctx.Setup(c => c.SessionId).Returns("form-7");
        _store.Setup(s => s.GetOrCreateAsync("form-7", It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var result = await MakeTool().VerifyFormAgainstPlanAsync(PlanId, FormId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual!.Contains("still pending"));
    }

    [Fact]
    public async Task VerifyForm_BadGuid_ReturnsFail()
    {
        SetupSession("form-8");
        var result = await MakeTool().VerifyFormAgainstPlanAsync(PlanId, "not-a-guid", CancellationToken.None);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "formId");
    }
}
