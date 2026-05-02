using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class RequestApprovalToolTests
{
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _session = new();
    private readonly RequestApprovalTool _tool;

    public RequestApprovalToolTests()
    {
        _session.SetupGet(s => s.SessionId).Returns("test-session");
        _store.Setup(s => s.GetOrCreateAsync("test-session", It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ProjectState());
        _tool = new RequestApprovalTool(_store.Object, _session.Object);
    }

    [Fact]
    public async Task RequestApprovalAsync_ReturnsConfirmationMessage()
    {
        var result = await _tool.RequestApprovalAsync(
            "CreateTable",
            "Create the Feedback table with 8 fields",
            "orange",
            "{\"tableName\":\"ctso_feedback\"}",
            CancellationToken.None);

        Assert.Contains("Approval requested for CreateTable", result);
        Assert.Contains("Create the Feedback table with 8 fields", result);
        Assert.Contains("Risk level: orange", result);
        Assert.Contains("Waiting for user decision", result);
    }

    [Fact]
    public async Task RequestApprovalAsync_IncludesAllRiskLevels()
    {
        foreach (var risk in new[] { "green", "yellow", "orange", "red" })
        {
            var result = await _tool.RequestApprovalAsync(
                "TestOp", "desc", risk, "{}", CancellationToken.None);

            Assert.Contains($"Risk level: {risk}", result);
        }
    }

    [Fact]
    public async Task RequestApprovalAsync_InstructsAgentToWait()
    {
        var result = await _tool.RequestApprovalAsync(
            "DeleteTable", "Delete everything", "red", "{}", CancellationToken.None);

        Assert.Contains("Do NOT proceed until the user responds", result);
    }

    [Fact]
    public async Task RequestApprovalAsync_RejectsUnknownPlanId()
    {
        // Empty PendingPlans → planId in details should be rejected.
        var result = await _tool.RequestApprovalAsync(
            "CreateField", "Add a field", "yellow",
            "{\"planId\":\"create_field_does_not_exist\"}",
            CancellationToken.None);

        Assert.Contains("ERROR", result);
        Assert.Contains("create_field_does_not_exist", result);
    }

    [Fact]
    public async Task RequestApprovalAsync_AcceptsKnownPlanId()
    {
        var state = new ProjectState();
        state.PendingPlans["create_field_real"] = "{\"SchemaName\":\"adi_x\"}";
        _store.Setup(s => s.GetOrCreateAsync("test-session", It.IsAny<CancellationToken>()))
              .ReturnsAsync(state);

        var result = await _tool.RequestApprovalAsync(
            "CreateField", "Add a field", "yellow",
            "{\"planId\":\"create_field_real\"}",
            CancellationToken.None);

        Assert.DoesNotContain("ERROR", result);
        Assert.Contains("Approval requested for CreateField", result);
    }
}
