using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanModifyFieldToolTests
{
    private static (PlanModifyFieldTool tool, Dictionary<string, ProjectState> store) NewTool()
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

        return (new PlanModifyFieldTool(mockStore.Object, mockCtx.Object), stored);
    }

    [Fact]
    public async Task PlanModifyField_DisplayName_ReturnsModification()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_description",
            modificationsJson: "{\"displayName\":\"Project Description\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Single(plan.Modifications);
        Assert.Equal("DisplayName", plan.Modifications[0].Property);
        Assert.Equal("Project Description", plan.Modifications[0].NewValue);
        Assert.Contains("DisplayName", store["test-session"].PendingPlans[plan.PlanId]);
    }

    [Fact]
    public async Task PlanModifyField_RequiredLevel_ApplicationRequired_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_status",
            modificationsJson: "{\"requiredLevel\":\"ApplicationRequired\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("ApplicationRequired"));
    }

    [Fact]
    public async Task PlanModifyField_DecreaseMaxLength_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"maxLength\":100}",
            currentMaxLength: 200,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Decreasing MaxLength"));
    }

    [Fact]
    public async Task PlanModifyField_MultipleModifications_ReturnsAll()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"displayName\":\"Project Name\",\"isSearchable\":true,\"isAuditEnabled\":true}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.Equal(3, plan.Modifications.Count);
    }

    [Fact]
    public async Task PlanModifyField_InvalidProperty_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"fieldType\":\"integer\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("cannot be modified"));
    }

    [Fact]
    public async Task PlanModifyField_RiskLevel_IsYellow()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"displayName\":\"New Name\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
