using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class UnassignAppRoleToolTests
{
    private static (UnassignAppRoleTool Tool, Mock<IDataverseClient> Client, ProjectState State) BuildTool()
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
        var tool = new UnassignAppRoleTool(client.Object, store.Object, ctx.Object, NullLogger<UnassignAppRoleTool>.Instance);
        return (tool, client, state);
    }

    [Fact]
    public async Task Unassign_HappyPath_FiresClientAndRecordsPlan()
    {
        var (tool, client, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        var result = await tool.UnassignAppRoleAsync(appId.ToString(), roleId.ToString(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.StartsWith("unassign_app_role_", result.PlanId, StringComparison.Ordinal);
        client.Verify(c => c.UnassignAppRoleAsync(appId, roleId, It.IsAny<CancellationToken>()), Times.Once);

        var doc = JsonSerializer.Deserialize<JsonElement>(state.ExecutedPlans[result.PlanId]);
        Assert.Equal("unassign", doc.GetProperty("action").GetString());
    }

    [Fact]
    public async Task Unassign_PushesReverseAssignOnOperationStack()
    {
        var (tool, _, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await tool.UnassignAppRoleAsync(appId.ToString(), roleId.ToString(), CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("UnassignAppRole", op.ToolName);
        Assert.Equal("AssignAppRole", op.ReverseOp!.ToolName);
    }
}
