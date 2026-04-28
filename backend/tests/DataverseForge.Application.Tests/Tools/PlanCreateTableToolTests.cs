using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateTableToolTests
{
    private static (PlanCreateTableTool tool, Dictionary<string, ProjectState> store) NewTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        var mockStore = new Mock<IProjectStateStore>();
        mockStore
            .Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        mockStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);

        var mockCtx = new Mock<ISessionContext>();
        mockCtx.SetupGet(c => c.SessionId).Returns("test-session");

        return (new PlanCreateTableTool(mockStore.Object, mockCtx.Object), stored);
    }

    [Fact]
    public async Task PlanCreateTable_ValidInput_ReturnsValidPlan()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateTableAsync(
            displayName: "Work Order",
            pluralDisplayName: "Work Orders",
            description: "Tracks maintenance work orders",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Work Order Title",
            primaryFieldMaxLength: 200,
            publisherPrefix: "cr123",
            solutionUniqueName: "MyTestSolution",
            hasNotes: true,
            hasActivities: false,
            changeTrackingEnabled: true,
            isAuditEnabled: true,
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("Work Order", plan.DisplayName);
        Assert.Equal("Work Orders", plan.PluralDisplayName);
        Assert.Equal("MyTestSolution", plan.SolutionUniqueName);
        Assert.NotEmpty(plan.PlanId);
        // Payload was stored server-side keyed by planId
        Assert.True(store["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanCreateTable_DerivedNames_AreCorrect()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateTableAsync(
            displayName: "Work Order",
            pluralDisplayName: "Work Orders",
            description: "Test",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Title",
            primaryFieldMaxLength: 100,
            publisherPrefix: "cr123",
            solutionUniqueName: "TestSol",
            hasNotes: false,
            hasActivities: false,
            changeTrackingEnabled: false,
            isAuditEnabled: false,
            CancellationToken.None);

        Assert.Equal("cr123_workorder", plan.TableLogicalName);
        Assert.Equal("cr123_WorkOrder", plan.TableSchemaName);
        Assert.Equal("cr123_workordername", plan.PrimaryFieldLogicalName);
    }

    [Fact]
    public async Task PlanCreateTable_PayloadContainsRequiredODataProperties()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateTableAsync(
            displayName: "Project",
            pluralDisplayName: "Projects",
            description: "Project tracking",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Project Name",
            primaryFieldMaxLength: 150,
            publisherPrefix: "cr123",
            solutionUniqueName: "TestSol",
            hasNotes: false,
            hasActivities: false,
            changeTrackingEnabled: false,
            isAuditEnabled: false,
            CancellationToken.None);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("Microsoft.Dynamics.CRM.EntityMetadata", payload);
        Assert.Contains("Microsoft.Dynamics.CRM.StringAttributeMetadata", payload);
        Assert.Contains("LocalizedLabels", payload);
        Assert.Contains("cr123_Project", payload);
    }

    [Fact]
    public async Task PlanCreateTable_RiskLevel_IsOrange()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateTableAsync(
            displayName: "Task",
            pluralDisplayName: "Tasks",
            description: "Task tracking",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Task Name",
            primaryFieldMaxLength: 100,
            publisherPrefix: "cr123",
            solutionUniqueName: "TestSol",
            hasNotes: false,
            hasActivities: false,
            changeTrackingEnabled: false,
            isAuditEnabled: false,
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
