using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GenerateChangelogEntryToolTests
{
    private readonly Mock<IProjectStateStore> _mockStateStore = new();
    private readonly Mock<ISessionContext> _mockSession = new();

    private GenerateChangelogEntryTool CreateTool() =>
        new(_mockStateStore.Object, _mockSession.Object);

    [Fact]
    public async Task GenerateAsync_NoOperations_ReturnsEmptyMessage()
    {
        var sessionId = "cl-1";
        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectState { ProjectId = sessionId });

        var tool = CreateTool();
        var result = await tool.GenerateChangelogEntryAsync("1.0.0", CancellationToken.None);

        Assert.Contains("No operations", result);
    }

    [Fact]
    public async Task GenerateAsync_WithCreateOperations_ReturnsAddedSection()
    {
        var sessionId = "cl-2";
        var state = new ProjectState { ProjectId = sessionId };
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op-1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = "DataModelBuilder",
            ToolName = "execute_create_table",
            IsReversible = true
        });

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateChangelogEntryAsync("1.0.0", CancellationToken.None);

        Assert.Contains("## [1.0.0]", result);
        Assert.Contains("### Added", result);
        Assert.Contains("DataModelBuilder", result);
    }

    [Fact]
    public async Task GenerateAsync_VersionIncludedInHeader()
    {
        var sessionId = "cl-3";
        var state = new ProjectState { ProjectId = sessionId };
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op-1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = "DataModelBuilder",
            ToolName = "execute_create_field"
        });

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateChangelogEntryAsync("2.1.0", CancellationToken.None);

        Assert.Contains("[2.1.0]", result);
    }

    [Fact]
    public async Task GenerateAsync_MixedOperations_SeparatesSections()
    {
        var sessionId = "cl-4";
        var state = new ProjectState { ProjectId = sessionId };
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op-1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = "DataModelBuilder",
            ToolName = "execute_create_table"
        });
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op-2",
            ExecutedAt = DateTime.UtcNow,
            AgentName = "DataModelBuilder",
            ToolName = "execute_delete_table"
        });

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateChangelogEntryAsync("1.0.0", CancellationToken.None);

        Assert.Contains("### Added", result);
        Assert.Contains("### Removed", result);
    }
}
