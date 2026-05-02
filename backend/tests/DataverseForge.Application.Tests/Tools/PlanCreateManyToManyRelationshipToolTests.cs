using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateManyToManyRelationshipToolTests
{
    private static PlanCreateManyToManyRelationshipTool MakeTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        var mockStore = new Mock<IProjectStateStore>();
        mockStore.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        mockStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var mockCtx = new Mock<ISessionContext>();
        mockCtx.SetupGet(c => c.SessionId).Returns("test-session");
        return new PlanCreateManyToManyRelationshipTool(mockStore.Object, mockCtx.Object);
    }

    private readonly PlanCreateManyToManyRelationshipTool _tool = MakeTool();

    [Fact]
    public async Task PlanM2M_ValidInput_ReturnsValidPlanWithErdFragment()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_student",
            secondTable: "cr123_course",
            publisherPrefix: "cr123",
            relationshipSchemaName: null,
            intersectTableName: null,
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_student", plan.FirstTable);
        Assert.Equal("cr123_course", plan.SecondTable);
        Assert.Equal("cr123_student_course", plan.RelationshipSchemaName);
        Assert.Equal("cr123_student_course", plan.IntersectTableName);
        Assert.Contains("}o--o{", plan.ErDiagramFragment);
        Assert.Contains("Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanM2M_CustomNames_UsesProvidedNames()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_student",
            secondTable: "cr123_course",
            publisherPrefix: "cr123",
            relationshipSchemaName: "cr123_enrollment",
            intersectTableName: "cr123_enrollment_intersect",
            CancellationToken.None);

        Assert.Equal("cr123_enrollment", plan.RelationshipSchemaName);
        Assert.Equal("cr123_enrollment_intersect", plan.IntersectTableName);
    }

    [Fact]
    public async Task PlanM2M_SelfReferencing_ReturnsWarning()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_employee",
            secondTable: "cr123_employee",
            publisherPrefix: "cr123",
            relationshipSchemaName: null,
            intersectTableName: null,
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Self-referencing"));
    }

    [Fact]
    public async Task PlanM2M_RiskLevel_IsOrange()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_student",
            secondTable: "cr123_course",
            publisherPrefix: "cr123",
            relationshipSchemaName: null,
            intersectTableName: null,
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
