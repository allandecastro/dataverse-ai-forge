using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateManyToManyRelationshipToolTests
{
    private const string PlanId = "create_many_to_many_relationship_test";
    private const string PayloadJson = "{\"@odata.type\":\"Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata\"}";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateManyToManyRelationshipTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateManyToManyRelationshipTool>.Instance);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
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
    public async Task ExecuteM2M_LooksUpPayloadByPlanId_AndCallsCreate()
    {
        SetupDefaults("exec-m2m-test-1");
        var tool = CreateTool();

        await tool.ExecuteCreateManyToManyRelationshipAsync(
            PlanId, "cr123_student_course", "cr123_student", "cr123_course", "cr123_student_course", "TestSolution", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateManyToManyRelationshipAsync(PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        _mockDataverseClient.Verify(
            c => c.AddSolutionComponentAsync(It.IsAny<Guid>(), SolutionComponentType.EntityRelationship, "TestSolution", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteM2M_AddsToStateRelationships_AndRemovesPlan()
    {
        var state = SetupDefaults("exec-m2m-test-2");
        var tool = CreateTool();

        await tool.ExecuteCreateManyToManyRelationshipAsync(
            PlanId, "cr123_student_course", "cr123_student", "cr123_course", "cr123_student_course", "TestSolution", CancellationToken.None);

        Assert.Single(state.Relationships);
        Assert.Equal("cr123_student_course", state.Relationships[0].SchemaName);
        Assert.Equal("ManyToMany", state.Relationships[0].RelationshipType);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteM2M_PushesToOperationStack()
    {
        var state = SetupDefaults("exec-m2m-test-3");
        var tool = CreateTool();

        await tool.ExecuteCreateManyToManyRelationshipAsync(
            PlanId, "cr123_student_course", "cr123_student", "cr123_course", "cr123_student_course", "TestSolution", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateManyToManyRelationship", op.ToolName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteM2M_ReturnsError_WhenPlanIdMissing()
    {
        var state = SetupDefaults("exec-m2m-test-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteCreateManyToManyRelationshipAsync(
            "missing", "cr123_student_course", "cr123_student", "cr123_course", "cr123_student_course", "TestSolution", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.CreateManyToManyRelationshipAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
