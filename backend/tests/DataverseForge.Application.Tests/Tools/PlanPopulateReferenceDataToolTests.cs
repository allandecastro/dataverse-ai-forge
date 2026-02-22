using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanPopulateReferenceDataToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private PlanPopulateReferenceDataTool CreateTool() =>
        new(_mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "cr123_category", "Category", DateTime.UtcNow));
        state.Fields.Add(new CreatedField(Guid.NewGuid(), "cr123_category", "cr123_name", "Name", "String (Required)", DateTime.UtcNow));

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task ValidInput_ReturnsPlan()
    {
        var sessionId = "plan-pop-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordsJson = "[{\"cr123_name\":\"Product Quality\"},{\"cr123_name\":\"Customer Service\"}]";

        var plan = await tool.PlanPopulateReferenceDataAsync(
            "cr123_category", "cr123_categories", recordsJson, CancellationToken.None);

        Assert.Equal("cr123_category", plan.TableLogicalName);
        Assert.Equal("cr123_categories", plan.EntitySetName);
        Assert.Equal(2, plan.RecordCount);
        Assert.Equal(2, plan.Records.Count);
    }

    [Fact]
    public async Task EmptyRecords_ReturnsWarning()
    {
        var sessionId = "plan-pop-test-2";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var plan = await tool.PlanPopulateReferenceDataAsync(
            "cr123_category", "cr123_categories", "[]", CancellationToken.None);

        Assert.Equal(0, plan.RecordCount);
        Assert.Contains(plan.Warnings, w => w.Contains("No records provided"));
    }

    [Fact]
    public async Task InvalidJson_ReturnsWarning()
    {
        var sessionId = "plan-pop-test-3";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var plan = await tool.PlanPopulateReferenceDataAsync(
            "cr123_category", "cr123_categories", "NOT_VALID_JSON", CancellationToken.None);

        Assert.Equal(0, plan.RecordCount);
        Assert.Contains(plan.Warnings, w => w.Contains("Could not parse records JSON"));
    }

    [Fact]
    public async Task TableNotInState_ReturnsWarning()
    {
        var sessionId = "plan-pop-test-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordsJson = "[{\"cr123_name\":\"Value1\"}]";

        var plan = await tool.PlanPopulateReferenceDataAsync(
            "cr123_nonexistent", "cr123_nonexistents", recordsJson, CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("not found in project state"));
    }

    [Fact]
    public async Task RiskLevel_IsYellow()
    {
        var sessionId = "plan-pop-test-5";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordsJson = "[{\"cr123_name\":\"Value1\"}]";

        var plan = await tool.PlanPopulateReferenceDataAsync(
            "cr123_category", "cr123_categories", recordsJson, CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
