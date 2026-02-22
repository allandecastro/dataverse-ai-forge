using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanGenerateTestDataToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private PlanGenerateTestDataTool CreateTool() =>
        new(_mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId, bool addTable = true)
    {
        var state = new ProjectState { ProjectId = sessionId };
        if (addTable)
        {
            state.Tables.Add(new CreatedTable(Guid.NewGuid(), "cr_workorder", "Work Order", DateTime.UtcNow));
        }
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task ValidInput_ReturnsPlan()
    {
        var sessionId = "plan-testdata-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordsJson = """[{"cr_name":"Record 1","cr_status":1},{"cr_name":"Record 2","cr_status":2}]""";

        var result = await tool.PlanGenerateTestDataAsync(
            "cr_workorder", "cr_workorders", 2, recordsJson, CancellationToken.None);

        Assert.Equal("cr_workorder", result.TableLogicalName);
        Assert.Equal("cr_workorders", result.EntitySetName);
        Assert.Equal(2, result.RecordCount);
        Assert.Equal(2, result.Records.Count);
    }

    [Fact]
    public async Task InvalidJson_ReturnsWarning()
    {
        var sessionId = "plan-testdata-2";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.PlanGenerateTestDataAsync(
            "cr_workorder", "cr_workorders", 5, "INVALID_JSON", CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Contains("Could not parse records JSON"));
    }

    [Fact]
    public async Task TableNotInState_ReturnsWarning()
    {
        var sessionId = "plan-testdata-3";
        SetupDefaults(sessionId, addTable: false);
        var tool = CreateTool();

        var recordsJson = """[{"cr_name":"Record 1"}]""";

        var result = await tool.PlanGenerateTestDataAsync(
            "cr_workorder", "cr_workorders", 1, recordsJson, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Contains("not found in project state"));
    }

    [Fact]
    public async Task Under20Records_RiskYellow()
    {
        var sessionId = "plan-testdata-4";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var recordsJson = """[{"cr_name":"Record 1"}]""";

        var result = await tool.PlanGenerateTestDataAsync(
            "cr_workorder", "cr_workorders", 10, recordsJson, CancellationToken.None);

        Assert.Equal("yellow", result.RiskLevel);
    }

    [Fact]
    public async Task Over20Records_RiskOrange()
    {
        var sessionId = "plan-testdata-5";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var records = Enumerable.Range(1, 25)
            .Select(i => $"{{\"cr_name\":\"Record {i}\"}}")
            .ToList();
        var recordsJson = $"[{string.Join(",", records)}]";

        var result = await tool.PlanGenerateTestDataAsync(
            "cr_workorder", "cr_workorders", 25, recordsJson, CancellationToken.None);

        Assert.Equal("orange", result.RiskLevel);
    }

    [Fact]
    public async Task Over50Records_WarnsAboutBatching()
    {
        var sessionId = "plan-testdata-6";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var records = Enumerable.Range(1, 55)
            .Select(i => $"{{\"cr_name\":\"Record {i}\"}}")
            .ToList();
        var recordsJson = $"[{string.Join(",", records)}]";

        var result = await tool.PlanGenerateTestDataAsync(
            "cr_workorder", "cr_workorders", 55, recordsJson, CancellationToken.None);

        Assert.Contains(result.Warnings, w => w.Contains("batching"));
        Assert.Equal("orange", result.RiskLevel);
    }
}
