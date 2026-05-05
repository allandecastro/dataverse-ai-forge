using System.Net;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class DeleteAppToolTests
{
    private static (DeleteAppTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored) BuildTool(Guid? trackedAppId = null)
    {
        var stored = new Dictionary<string, ProjectState>();
        var seed = new ProjectState { ProjectId = "test-session" };
        if (trackedAppId is not null)
            seed.Apps.Add(new CreatedApp(trackedAppId.Value, "Demo", "eppc_demo", DateTime.UtcNow));
        stored["test-session"] = seed;

        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        var client = new Mock<IDataverseClient>();
        var tool = new DeleteAppTool(client.Object, store.Object, ctx.Object, NullLogger<DeleteAppTool>.Instance);
        return (tool, client, stored);
    }

    [Fact]
    public async Task DeleteApp_HappyPath_RemovesFromStateAndPushesOperation()
    {
        var appId = Guid.NewGuid();
        var (tool, client, stored) = BuildTool(trackedAppId: appId);

        client.Setup(c => c.DeleteAppAsync(appId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await tool.DeleteAppAsync(appId.ToString(), "Demo", CancellationToken.None);

        Assert.Contains($"App 'Demo' (id={appId}) deleted", result);

        var state = stored["test-session"];
        Assert.Empty(state.Apps);
        Assert.Single(state.OperationStack);
        Assert.Equal("DeleteApp", state.OperationStack.Peek().ToolName);
    }

    [Fact]
    public async Task DeleteApp_ReturnsError_OnInvalidGuid()
    {
        var (tool, client, _) = BuildTool();

        var result = await tool.DeleteAppAsync("not-a-guid", "Demo", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        client.Verify(c => c.DeleteAppAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteApp_SurfacesManagedAppRejection()
    {
        var appId = Guid.NewGuid();
        var (tool, client, _) = BuildTool();

        client.Setup(c => c.DeleteAppAsync(appId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Delete app (...) failed: HTTP 403 — Cannot delete a managed component.", inner: null, HttpStatusCode.Forbidden));

        var result = await tool.DeleteAppAsync(appId.ToString(), "Sales Hub", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("Cannot delete a managed component", result);
        Assert.Contains("managed solution", result);
    }
}
