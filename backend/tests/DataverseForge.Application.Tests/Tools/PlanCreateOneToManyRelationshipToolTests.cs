using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateOneToManyRelationshipToolTests
{
    private static PlanCreateOneToManyRelationshipTool MakeTool()
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
        return new PlanCreateOneToManyRelationshipTool(mockStore.Object, mockCtx.Object);
    }

    private readonly PlanCreateOneToManyRelationshipTool _tool = MakeTool();

    [Fact]
    public async Task PlanCreateRelationship_ValidInput_ReturnsValidPlanWithErdFragment()
    {
        var plan = await _tool.PlanCreateOneToManyRelationshipAsync(
            primaryTable: "cr123_project",
            relatedTable: "cr123_task",
            lookupFieldDisplayName: "Project",
            publisherPrefix: "cr123",
            cascadeAssign: "NoCascade",
            cascadeDelete: "RemoveLink",
            cascadeReparent: "NoCascade",
            cascadeMerge: "NoCascade",
            cascadeShare: "NoCascade",
            cascadeUnshare: "NoCascade",
            lookupBehavior: "Referential",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_project", plan.PrimaryTable);
        Assert.Equal("cr123_task", plan.RelatedTable);
        Assert.Contains("cr123_projectid", plan.LookupFieldLogicalName);
        Assert.Contains("||--o{", plan.ErDiagramFragment);
        Assert.Contains("cr123_project", plan.ErDiagramFragment);
        Assert.Contains("cr123_task", plan.ErDiagramFragment);
        Assert.Contains("Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateRelationship_CascadeDelete_ReturnsWarning()
    {
        var plan = await _tool.PlanCreateOneToManyRelationshipAsync(
            primaryTable: "cr123_project",
            relatedTable: "cr123_task",
            lookupFieldDisplayName: "Project",
            publisherPrefix: "cr123",
            cascadeAssign: "NoCascade",
            cascadeDelete: "Cascade",
            cascadeReparent: "NoCascade",
            cascadeMerge: "NoCascade",
            cascadeShare: "NoCascade",
            cascadeUnshare: "NoCascade",
            lookupBehavior: "Referential",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Cascade delete"));
    }

    [Fact]
    public async Task PlanCreateRelationship_RiskLevel_IsOrange()
    {
        var plan = await _tool.PlanCreateOneToManyRelationshipAsync(
            primaryTable: "cr123_project",
            relatedTable: "cr123_task",
            lookupFieldDisplayName: "Project",
            publisherPrefix: "cr123",
            cascadeAssign: "NoCascade",
            cascadeDelete: "RemoveLink",
            cascadeReparent: "NoCascade",
            cascadeMerge: "NoCascade",
            cascadeShare: "NoCascade",
            cascadeUnshare: "NoCascade",
            lookupBehavior: "Referential",
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
