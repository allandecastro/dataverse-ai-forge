using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateOneToManyRelationshipToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateOneToManyRelationshipTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateOneToManyRelationshipAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteCreateRelationship_CallsCreateRelationshipAndAddSolutionComponent()
    {
        var sessionId = "exec-rel-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateOneToManyRelationshipAsync(
            "{}", "cr123_project_task", "cr123_project", "cr123_task", "TestSolution", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateOneToManyRelationshipAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(
            c => c.AddSolutionComponentAsync(It.IsAny<Guid>(), 10, "TestSolution", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateRelationship_AddsToStateRelationships()
    {
        var sessionId = "exec-rel-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateOneToManyRelationshipAsync(
            "{}", "cr123_project_task", "cr123_project", "cr123_task", "TestSolution", CancellationToken.None);

        Assert.Single(state.Relationships);
        Assert.Equal("cr123_project_task", state.Relationships[0].SchemaName);
        Assert.Equal("cr123_project", state.Relationships[0].PrimaryTable);
        Assert.Equal("cr123_task", state.Relationships[0].RelatedTable);
        Assert.Equal("OneToMany", state.Relationships[0].RelationshipType);
    }

    [Fact]
    public async Task ExecuteCreateRelationship_PushesToOperationStack()
    {
        var sessionId = "exec-rel-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateOneToManyRelationshipAsync(
            "{}", "cr123_project_task", "cr123_project", "cr123_task", "TestSolution", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateOneToManyRelationship", op.ToolName);
        Assert.True(op.IsReversible);
    }
}
