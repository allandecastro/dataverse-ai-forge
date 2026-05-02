using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateOneToManyRelationshipToolTests
{
    private const string PlanId = "create_one_to_many_relationship_test";
    private const string PayloadJson = "{\"@odata.type\":\"Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateOneToManyRelationshipTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateOneToManyRelationshipTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
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
    public async Task ExecuteCreateRelationship_LooksUpPayloadByPlanId_AndCallsCreate()
    {
        SetupDefaults("exec-rel-test-1");
        var tool = CreateTool();

        await tool.ExecuteCreateOneToManyRelationshipAsync(
            PlanId, "cr123_project_task", "cr123_project", "cr123_task", "TestSolution", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateOneToManyRelationshipAsync(PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(
            c => c.AddSolutionComponentAsync(It.IsAny<Guid>(), SolutionComponentType.EntityRelationship, "TestSolution", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateRelationship_AddsToStateRelationships_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-rel-test-2");
        var tool = CreateTool();

        await tool.ExecuteCreateOneToManyRelationshipAsync(
            PlanId, "cr123_project_task", "cr123_project", "cr123_task", "TestSolution", CancellationToken.None);

        Assert.Single(state.Relationships);
        Assert.Equal("cr123_project_task", state.Relationships[0].SchemaName);
        Assert.Equal("cr123_project", state.Relationships[0].PrimaryTable);
        Assert.Equal("cr123_task", state.Relationships[0].RelatedTable);
        Assert.Equal("OneToMany", state.Relationships[0].RelationshipType);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteCreateRelationship_PushesToOperationStack()
    {
        var state = SetupDefaults("exec-rel-test-3");
        var tool = CreateTool();

        await tool.ExecuteCreateOneToManyRelationshipAsync(
            PlanId, "cr123_project_task", "cr123_project", "cr123_task", "TestSolution", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateOneToManyRelationship", op.ToolName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateRelationship_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-rel-test-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteCreateOneToManyRelationshipAsync(
            "missing", "cr123_project_task", "cr123_project", "cr123_task", "TestSolution", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.CreateOneToManyRelationshipAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
