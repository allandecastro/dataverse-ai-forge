using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateSecurityRoleToolTests
{
    private const string PlanId = "create_security_role_test";
    private static readonly Guid PrivId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid BuId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    // Composite payload mirrors PlanCreateSecurityRoleTool's output shape — keeps
    // the test independent of the planner so a planner change doesn't cascade.
    private static readonly string PayloadJson = $$"""
        {
          "role": {
            "name": "Feedback Reviewer",
            "description": "Read at BU",
            "businessunitid@odata.bind": "/businessunits({{BuId}})"
          },
          "privileges": [
            { "PrivilegeId": "{{PrivId}}", "Depth": "Local", "Name": "prvReadadc_feedback" }
          ]
        }
        """;

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateSecurityRoleTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateSecurityRoleTool>.Instance);

    private ProjectState SetupDefaults(string sessionId, bool seedPending = true, bool seedExecuted = false)
    {
        var state = new ProjectState { ProjectId = sessionId };
        if (seedPending) state.PendingPlans[PlanId] = PayloadJson;
        if (seedExecuted) state.ExecutedPlans[PlanId] = PayloadJson;

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        _mockProjectStateStore
            .Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockDataverseClient
            .Setup(c => c.CreateRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        _mockDataverseClient
            .Setup(c => c.AddPrivilegesToRoleAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<RolePrivilegeAssignment>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return state;
    }

    [Fact]
    public async Task ExecuteCreateRole_HappyPath_CallsCreateAndAddPrivileges()
    {
        SetupDefaults("session-1");
        var tool = CreateTool();

        var result = await tool.ExecuteCreateSecurityRoleAsync(PlanId, CancellationToken.None);

        Assert.Contains("created with RoleId=", result);
        _mockDataverseClient.Verify(c => c.CreateRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(c => c.AddPrivilegesToRoleAsync(
            It.IsAny<Guid>(),
            It.Is<IReadOnlyList<RolePrivilegeAssignment>>(p => p.Count == 1 && p[0].PrivilegeId == PrivId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateRole_MovesPlanToExecutedAndPushesOperation()
    {
        var state = SetupDefaults("session-2");
        var tool = CreateTool();

        await tool.ExecuteCreateSecurityRoleAsync(PlanId, CancellationToken.None);

        Assert.False(state.PendingPlans.ContainsKey(PlanId));
        Assert.True(state.ExecutedPlans.ContainsKey(PlanId));
        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateSecurityRole", op.ToolName);
        // Commit 3 lit up undo support: IsReversible=true with a
        // DeleteSecurityRole reverse-op the dispatcher knows how to run.
        Assert.True(op.IsReversible);
        Assert.Equal("DeleteSecurityRole", op.ReverseOp?.ToolName);
    }

    [Fact]
    public async Task ExecuteCreateRole_AlreadyExecuted_ReturnsAlreadyExecuted()
    {
        SetupDefaults("session-3", seedPending: false, seedExecuted: true);
        var tool = CreateTool();

        var result = await tool.ExecuteCreateSecurityRoleAsync(PlanId, CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        _mockDataverseClient.Verify(c => c.CreateRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateRole_NotFound_ReturnsError()
    {
        SetupDefaults("session-4", seedPending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteCreateSecurityRoleAsync(PlanId, CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("not found", result);
        _mockDataverseClient.Verify(c => c.CreateRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateRole_RejectedPlanId_Skips()
    {
        SetupDefaults("session-5", seedPending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteCreateSecurityRoleAsync(
            "rejected_role_exists_abcdef", CancellationToken.None);

        Assert.StartsWith("SKIPPED:", result);
        _mockDataverseClient.Verify(c => c.CreateRoleAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
