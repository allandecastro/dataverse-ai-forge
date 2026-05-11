using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteUpdateSecurityRoleToolTests
{
    private const string PlanId = "update_security_role_test";
    private static readonly Guid RoleId = Guid.Parse("bbbb1111-bbbb-1111-bbbb-111111111111");
    private static readonly Guid PrivAddId = Guid.Parse("bbbb2222-bbbb-2222-bbbb-222222222222");
    private static readonly Guid PrivRemoveAId = Guid.Parse("bbbb3333-bbbb-3333-bbbb-333333333333");
    private static readonly Guid PrivRemoveBId = Guid.Parse("bbbb4444-bbbb-4444-bbbb-444444444444");

    private static readonly string PayloadJson = $$"""
        {
          "roleId": "{{RoleId}}",
          "rolePatch": { "name": "Renamed Role" },
          "add": [
            { "PrivilegeId": "{{PrivAddId}}", "Depth": "Local", "Name": "prvAppendadc_feedback" }
          ],
          "remove": [
            { "PrivilegeId": "{{PrivRemoveAId}}", "Depth": "Local", "Name": "prvShareadc_feedback" },
            { "PrivilegeId": "{{PrivRemoveBId}}", "Depth": "Local", "Name": "prvAssignadc_feedback" }
          ],
          "target": [
            { "PrivilegeId": "{{PrivAddId}}", "Depth": "Local", "Name": "prvAppendadc_feedback" }
          ],
          "expectedName": "Renamed Role"
        }
        """;

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteUpdateSecurityRoleTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteUpdateSecurityRoleTool>.Instance);

    private ProjectState SetupDefaults(string sessionId, bool seedPending = true)
    {
        var state = new ProjectState { ProjectId = sessionId };
        if (seedPending) state.PendingPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _mockProjectStateStore.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockDataverseClient.Setup(c => c.UpdateRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockDataverseClient.Setup(c => c.AddPrivilegesToRoleAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<RolePrivilegeAssignment>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockDataverseClient.Setup(c => c.RemovePrivilegeFromRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return state;
    }

    [Fact]
    public async Task ExecuteUpdate_HappyPath_PatchesAddsAndRemoves()
    {
        var state = SetupDefaults("session-update-1");
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSecurityRoleAsync(PlanId, CancellationToken.None);

        Assert.Contains("updated successfully", result);
        _mockDataverseClient.Verify(c => c.UpdateRoleAsync(RoleId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(c => c.AddPrivilegesToRoleAsync(RoleId,
            It.Is<IReadOnlyList<RolePrivilegeAssignment>>(p => p.Count == 1 && p[0].PrivilegeId == PrivAddId),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(c => c.RemovePrivilegeFromRoleAsync(RoleId, PrivRemoveAId, It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(c => c.RemovePrivilegeFromRoleAsync(RoleId, PrivRemoveBId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(state.ExecutedPlans.ContainsKey(PlanId));
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteUpdate_PartialFailureOnRemoveLoop_ReturnsPartial()
    {
        SetupDefaults("session-update-2");
        // Make the first remove fail; the second should still attempt.
        _mockDataverseClient.Setup(c => c.RemovePrivilegeFromRoleAsync(RoleId, PrivRemoveAId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse 500 — internal error"));
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSecurityRoleAsync(PlanId, CancellationToken.None);

        Assert.StartsWith("PARTIAL:", result);
        Assert.Contains("Remove failed", result);
        // The second privilege should still be attempted — partial-failure semantics.
        _mockDataverseClient.Verify(c => c.RemovePrivilegeFromRoleAsync(RoleId, PrivRemoveBId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteUpdate_AlreadyExecuted_ReturnsAlreadyExecuted()
    {
        var state = new ProjectState { ProjectId = "session-update-3" };
        state.ExecutedPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns("session-update-3");
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync("session-update-3", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSecurityRoleAsync(PlanId, CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        _mockDataverseClient.Verify(c => c.UpdateRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteUpdate_RejectedPlanId_Skips()
    {
        SetupDefaults("session-update-4", seedPending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteUpdateSecurityRoleAsync("rejected_update_role_managed_xyz", CancellationToken.None);

        Assert.StartsWith("SKIPPED:", result);
        _mockDataverseClient.Verify(c => c.UpdateRoleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
