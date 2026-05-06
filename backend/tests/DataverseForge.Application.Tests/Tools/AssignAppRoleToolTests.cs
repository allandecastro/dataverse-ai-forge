using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class AssignAppRoleToolTests
{
    private static (AssignAppRoleTool Tool, Mock<IDataverseClient> Client, ProjectState State) BuildTool()
    {
        var state = new ProjectState { ProjectId = "test-session" };
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        var client = new Mock<IDataverseClient>();
        var tool = new AssignAppRoleTool(client.Object, store.Object, ctx.Object, NullLogger<AssignAppRoleTool>.Instance);
        return (tool, client, state);
    }

    [Fact]
    public async Task Assign_HappyPath_FiresClientAndRecordsPlan()
    {
        var (tool, client, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var result = await tool.AssignAppRoleAsync(appId.ToString(), roleId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.StartsWith("assign_app_role_", result.PlanId, StringComparison.Ordinal);
        client.Verify(c => c.AssignAppRoleAsync(appId, roleId, It.IsAny<CancellationToken>()), Times.Once);

        // Plan was recorded in ExecutedPlans with the right shape.
        Assert.True(state.ExecutedPlans.ContainsKey(result.PlanId));
        var doc = JsonSerializer.Deserialize<JsonElement>(state.ExecutedPlans[result.PlanId]);
        Assert.Equal("assign", doc.GetProperty("action").GetString());
        Assert.Equal(appId.ToString(), doc.GetProperty("appId").GetString());
        Assert.Equal(roleId.ToString(), doc.GetProperty("roleId").GetString());
    }

    [Fact]
    public async Task Assign_RejectsInvalidAppId()
    {
        var (tool, client, _) = BuildTool();

        var result = await tool.AssignAppRoleAsync("not-a-guid", Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(string.Empty, result.PlanId);
        Assert.Contains("appId", result.Error, StringComparison.OrdinalIgnoreCase);
        client.Verify(c => c.AssignAppRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_RejectsInvalidRoleId()
    {
        var (tool, client, _) = BuildTool();

        var result = await tool.AssignAppRoleAsync(Guid.NewGuid().ToString(), "00000000-0000-0000-0000-000000000000", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("roleId", result.Error, StringComparison.OrdinalIgnoreCase);
        client.Verify(c => c.AssignAppRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Assign_OnClientFailure_SurfacesError()
    {
        var (tool, client, state) = BuildTool();
        client.Setup(c => c.AssignAppRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse 403 — privilege missing"));

        var result = await tool.AssignAppRoleAsync(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("403", result.Error);
        // Plan must NOT be recorded when the underlying call failed —
        // otherwise the verifier would see a "assign" action but the live
        // membership wouldn't reflect it (false-FAIL).
        Assert.Empty(state.ExecutedPlans);
    }

    [Fact]
    public async Task Assign_PushesReversibleOperationOnStack()
    {
        var (tool, _, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await tool.AssignAppRoleAsync(appId.ToString(), roleId.ToString(), CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("AssignAppRole", op.ToolName);
        Assert.True(op.IsReversible);
        Assert.NotNull(op.ReverseOp);
        Assert.Equal("UnassignAppRole", op.ReverseOp!.ToolName);
    }
}
