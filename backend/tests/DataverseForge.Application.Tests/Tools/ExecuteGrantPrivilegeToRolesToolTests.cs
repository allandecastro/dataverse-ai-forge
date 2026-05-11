using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteGrantPrivilegeToRolesToolTests
{
    private const string PlanId = "grant_privilege_to_roles_test";
    private static readonly Guid PrivId = Guid.Parse("eeee1111-eeee-1111-eeee-111111111111");
    private static readonly Guid RoleAId = Guid.Parse("eeee2222-eeee-2222-eeee-222222222222");
    private static readonly Guid RoleBId = Guid.Parse("eeee3333-eeee-3333-eeee-333333333333");

    private static readonly string PayloadJson = $$"""
        {
          "privilege": {
            "PrivilegeId": "{{PrivId}}",
            "Depth": "Local",
            "Name": "prvReadAccount",
            "AccessRight": "Read"
          },
          "roleIds": ["{{RoleAId}}", "{{RoleBId}}"]
        }
        """;

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteGrantPrivilegeToRolesTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteGrantPrivilegeToRolesTool>.Instance);

    private ProjectState SetupDefaults(string sessionId, bool seedPending = true)
    {
        var state = new ProjectState { ProjectId = sessionId };
        if (seedPending) state.PendingPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _mockProjectStateStore.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockDataverseClient.Setup(c => c.AddPrivilegesToRoleAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<RolePrivilegeAssignment>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return state;
    }

    [Fact]
    public async Task ExecuteBulkGrant_HappyPath_LoopsAddPrivileges()
    {
        var state = SetupDefaults("bulk-exec-1");
        var tool = CreateTool();

        var result = await tool.ExecuteGrantPrivilegeToRolesAsync(PlanId, CancellationToken.None);

        Assert.Contains("Granted", result);
        // One AddPrivilegesRole call per role in the batch.
        _mockDataverseClient.Verify(c => c.AddPrivilegesToRoleAsync(RoleAId,
            It.Is<IReadOnlyList<RolePrivilegeAssignment>>(p => p.Count == 1 && p[0].PrivilegeId == PrivId),
            It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(c => c.AddPrivilegesToRoleAsync(RoleBId,
            It.Is<IReadOnlyList<RolePrivilegeAssignment>>(p => p.Count == 1 && p[0].PrivilegeId == PrivId),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(state.ExecutedPlans.ContainsKey(PlanId));
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteBulkGrant_PartialFailure_OtherRolesStillAttempted()
    {
        SetupDefaults("bulk-exec-2");
        // Role A fails; Role B must still be tried — partial-failure semantics.
        _mockDataverseClient.Setup(c => c.AddPrivilegesToRoleAsync(RoleAId, It.IsAny<IReadOnlyList<RolePrivilegeAssignment>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse 500"));
        var tool = CreateTool();

        var result = await tool.ExecuteGrantPrivilegeToRolesAsync(PlanId, CancellationToken.None);

        Assert.StartsWith("PARTIAL:", result);
        Assert.Contains(RoleAId.ToString(), result);
        _mockDataverseClient.Verify(c => c.AddPrivilegesToRoleAsync(RoleBId, It.IsAny<IReadOnlyList<RolePrivilegeAssignment>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteBulkGrant_AlreadyExecuted_ReturnsAlreadyExecuted()
    {
        var state = new ProjectState { ProjectId = "bulk-exec-3" };
        state.ExecutedPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns("bulk-exec-3");
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync("bulk-exec-3", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        var tool = CreateTool();

        var result = await tool.ExecuteGrantPrivilegeToRolesAsync(PlanId, CancellationToken.None);

        Assert.StartsWith("ALREADY_EXECUTED:", result);
        _mockDataverseClient.Verify(c => c.AddPrivilegesToRoleAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<RolePrivilegeAssignment>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteBulkGrant_RejectedPlanId_Skips()
    {
        SetupDefaults("bulk-exec-4", seedPending: false);
        var tool = CreateTool();

        var result = await tool.ExecuteGrantPrivilegeToRolesAsync("rejected_bulk_grant_invalid_xyz", CancellationToken.None);

        Assert.StartsWith("SKIPPED:", result);
        _mockDataverseClient.Verify(c => c.AddPrivilegesToRoleAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<RolePrivilegeAssignment>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
