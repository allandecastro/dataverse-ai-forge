using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyViewAgainstPlanToolTests
{
    private const string PlanId = "create_view_test1";
    private static readonly Guid ViewId = Guid.Parse("22222222-bbbb-2222-bbbb-222222222222");
    private const string FetchXml = "<fetch><entity name='account'><attribute name='name' /></entity></fetch>";
    private const string LayoutXml = "<grid><row><cell name='name' width='200' /></row></grid>";
    private static readonly string PlannedPayload = $$"""
        {
          "name": "Active Accounts",
          "returnedtypecode": "account",
          "fetchxml": "{{FetchXml}}",
          "layoutxml": "{{LayoutXml}}"
        }
        """;

    private readonly Mock<IDataverseClient> _dataverse = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private VerifyViewAgainstPlanTool MakeTool() =>
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
    public async Task VerifyView_Matches_ReturnsPass()
    {
        SetupSession("view-1");
        _dataverse.Setup(c => c.GetViewsListAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ViewInfo>
            {
                new(ViewId, "Active Accounts", "Default", FetchXml, LayoutXml),
            });

        var result = await MakeTool().VerifyViewAgainstPlanAsync(PlanId, ViewId.ToString(), CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
        Assert.True(result.Exists);
        Assert.Empty(result.Discrepancies);
    }

    [Fact]
    public async Task VerifyView_NotFoundOnTable_ReturnsFail()
    {
        SetupSession("view-2");
        _dataverse.Setup(c => c.GetViewsListAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ViewInfo>());

        var result = await MakeTool().VerifyViewAgainstPlanAsync(PlanId, ViewId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "view");
    }

    [Fact]
    public async Task VerifyView_FetchXmlDiverges_ReturnsFail()
    {
        SetupSession("view-3");
        _dataverse.Setup(c => c.GetViewsListAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ViewInfo>
            {
                new(ViewId, "Active Accounts", "Default",
                    "<fetch><entity name='account'><attribute name='accountnumber' /></entity></fetch>",
                    LayoutXml),
            });

        var result = await MakeTool().VerifyViewAgainstPlanAsync(PlanId, ViewId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "fetchxml");
    }

    [Fact]
    public async Task VerifyView_NameMismatch_ReturnsFail()
    {
        SetupSession("view-4");
        _dataverse.Setup(c => c.GetViewsListAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ViewInfo>
            {
                new(ViewId, "Renamed Accounts", "Default", FetchXml, LayoutXml),
            });

        var result = await MakeTool().VerifyViewAgainstPlanAsync(PlanId, ViewId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "name");
    }

    [Fact]
    public async Task VerifyView_WrongPrefix_ReturnsFailWithSuggestion()
    {
        SetupSession("view-5");
        var result = await MakeTool().VerifyViewAgainstPlanAsync(
            "create_form_xyz", ViewId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual!.Contains("verify_form_against_plan"));
    }

    [Fact]
    public async Task VerifyView_RejectedPlan_ReturnsFailWithClearMessage()
    {
        SetupSession("view-6");
        var result = await MakeTool().VerifyViewAgainstPlanAsync(
            "rejected_view_invalid_abc", ViewId.ToString(), CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual!.Contains("REJECTED PLAN"));
    }
}
