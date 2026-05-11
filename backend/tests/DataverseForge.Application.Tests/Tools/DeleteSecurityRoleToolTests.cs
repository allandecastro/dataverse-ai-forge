using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class DeleteSecurityRoleToolTests
{
    private static readonly Guid RoleId = Guid.Parse("cccc1111-cccc-1111-cccc-111111111111");
    private static readonly Guid BuId = Guid.Parse("cccc2222-cccc-2222-cccc-222222222222");

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private DeleteSecurityRoleTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<DeleteSecurityRoleTool>.Instance);

    private ProjectState SetupDefaults(
        string sessionId,
        bool isManaged = false,
        bool isAutoAssigned = false,
        int assignedPrincipals = 0,
        bool exists = true)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _mockProjectStateStore.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _mockDataverseClient.Setup(c => c.GetSecurityRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists
                ? new SecurityRoleDetails(
                    Info: new SecurityRoleInfo(RoleId, "Feedback Reviewer", null, BuId, "Root", isManaged, isAutoAssigned, null, null, null),
                    ParentRoleId: null,
                    IsCustomizable: !isManaged)
                : null);
        _mockDataverseClient.Setup(c => c.CountUsersAndTeamsForRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assignedPrincipals);
        _mockDataverseClient.Setup(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return state;
    }

    [Fact]
    public async Task DeleteRole_HappyPath_CallsDelete()
    {
        var state = SetupDefaults("delete-session-1");
        var tool = CreateTool();

        var result = await tool.DeleteSecurityRoleAsync(RoleId, CancellationToken.None);

        Assert.Contains("deleted", result, StringComparison.OrdinalIgnoreCase);
        _mockDataverseClient.Verify(c => c.DeleteRoleAsync(RoleId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("DeleteSecurityRole", op.ToolName);
        // Delete is not reversible — recreating gives a fresh GUID + zero
        // privileges; same caveat as DeleteFieldTool.
        Assert.False(op.IsReversible);
    }

    [Fact]
    public async Task DeleteRole_AssignedToUsersOrTeams_Refuses()
    {
        SetupDefaults("delete-session-2", assignedPrincipals: 3);
        var tool = CreateTool();

        var result = await tool.DeleteSecurityRoleAsync(RoleId, CancellationToken.None);

        Assert.StartsWith("REFUSED:", result);
        Assert.Contains("3 active user/team", result);
        _mockDataverseClient.Verify(c => c.DeleteRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteRole_ManagedRole_Refuses()
    {
        SetupDefaults("delete-session-3", isManaged: true);
        var tool = CreateTool();

        var result = await tool.DeleteSecurityRoleAsync(RoleId, CancellationToken.None);

        Assert.StartsWith("REFUSED:", result);
        Assert.Contains("managed", result);
        // Managed pre-check runs BEFORE the count probe, so the count call
        // must not even be issued.
        _mockDataverseClient.Verify(c => c.CountUsersAndTeamsForRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockDataverseClient.Verify(c => c.DeleteRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteRole_AutoAssignedRole_Refuses()
    {
        SetupDefaults("delete-session-4", isAutoAssigned: true);
        var tool = CreateTool();

        var result = await tool.DeleteSecurityRoleAsync(RoleId, CancellationToken.None);

        Assert.StartsWith("REFUSED:", result);
        Assert.Contains("auto-assigned", result);
        _mockDataverseClient.Verify(c => c.DeleteRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteRole_RoleNotFound_NoOp()
    {
        SetupDefaults("delete-session-5", exists: false);
        var tool = CreateTool();

        var result = await tool.DeleteSecurityRoleAsync(RoleId, CancellationToken.None);

        Assert.StartsWith("NO_OP:", result);
        _mockDataverseClient.Verify(c => c.DeleteRoleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
