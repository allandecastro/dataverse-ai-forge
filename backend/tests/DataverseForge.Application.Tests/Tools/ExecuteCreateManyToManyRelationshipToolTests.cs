using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateManyToManyRelationshipToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateManyToManyRelationshipTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateManyToManyRelationshipTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateManyToManyRelationshipAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return state;
    }

    [Fact]
    public async Task ExecuteM2M_CallsCreateAndAddSolutionComponent()
    {
        var sessionId = "exec-m2m-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateManyToManyRelationshipAsync(
            "{}", "cr123_student_course", "cr123_student", "cr123_course", "cr123_student_course", "TestSolution", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateManyToManyRelationshipAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(
            c => c.AddSolutionComponentAsync(It.IsAny<Guid>(), 10, "TestSolution", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteM2M_AddsToStateRelationships()
    {
        var sessionId = "exec-m2m-test-2";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateManyToManyRelationshipAsync(
            "{}", "cr123_student_course", "cr123_student", "cr123_course", "cr123_student_course", "TestSolution", CancellationToken.None);

        Assert.Single(state.Relationships);
        Assert.Equal("cr123_student_course", state.Relationships[0].SchemaName);
        Assert.Equal("ManyToMany", state.Relationships[0].RelationshipType);
    }

    [Fact]
    public async Task ExecuteM2M_PushesToOperationStack()
    {
        var sessionId = "exec-m2m-test-3";
        var state = SetupDefaults(sessionId);
        var tool = CreateTool();

        await tool.ExecuteCreateManyToManyRelationshipAsync(
            "{}", "cr123_student_course", "cr123_student", "cr123_course", "cr123_student_course", "TestSolution", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateManyToManyRelationship", op.ToolName);
        Assert.True(op.IsReversible);
    }
}
