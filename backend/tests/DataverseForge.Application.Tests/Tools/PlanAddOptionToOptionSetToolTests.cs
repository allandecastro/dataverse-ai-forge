using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanAddOptionToOptionSetToolTests
{
    private static (PlanAddOptionToOptionSetTool tool, Dictionary<string, ProjectState> store) NewTool()
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

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetGlobalOptionSetOptionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OptionInfo>());
        mockClient
            .Setup(c => c.GetLocalOptionSetOptionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<OptionInfo>());

        return (new PlanAddOptionToOptionSetTool(mockStore.Object, mockCtx.Object, mockClient.Object), stored);
    }

    [Fact]
    public async Task PlanAddOption_GlobalOptionSet_ReturnsValidPlan()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanAddOptionToOptionSetAsync(
            optionSetName: "cr123_priority",
            isGlobal: true,
            tableLogicalName: "",
            fieldLogicalName: "",
            newOptionValue: 100003,
            newOptionLabel: "Critical",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.True(plan.IsGlobal);
        Assert.Equal(100003, plan.NewOptionValue);
        Assert.Equal("Critical", plan.NewOptionLabel);
        Assert.NotEmpty(plan.PlanId);
        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("OptionSetName", payload);
        Assert.Contains("cr123_priority", payload);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanAddOption_LocalOptionSet_MissingTable_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanAddOptionToOptionSetAsync(
            optionSetName: "local_status",
            isGlobal: false,
            tableLogicalName: "",
            fieldLogicalName: "",
            newOptionValue: 100000,
            newOptionLabel: "Active",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Table logical name"));
    }

    [Fact]
    public async Task PlanAddOption_RiskLevel_IsGreen()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanAddOptionToOptionSetAsync(
            optionSetName: "cr123_priority",
            isGlobal: true,
            tableLogicalName: "",
            fieldLogicalName: "",
            newOptionValue: 100003,
            newOptionLabel: "Critical",
            CancellationToken.None);

        Assert.Equal("green", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanAddOption_StatusCodeField_WarnsAndSuggestsStatusOption()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanAddOptionToOptionSetAsync(
            optionSetName: "adc_vehicle_statuscode",
            isGlobal: false,
            tableLogicalName: "adc_vehicle",
            fieldLogicalName: "statuscode",
            newOptionValue: 100000003,
            newOptionLabel: "Maintenance",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("plan_add_status_option"));
    }
}
