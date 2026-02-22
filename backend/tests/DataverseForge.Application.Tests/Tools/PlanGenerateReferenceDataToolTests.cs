using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanGenerateReferenceDataToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private PlanGenerateReferenceDataTool CreateTool() =>
        new(_mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "cr123_category", "Category", DateTime.UtcNow));
        state.Fields.Add(new CreatedField(Guid.NewGuid(), "cr123_category", "cr123_name", "Name", "String", DateTime.UtcNow));
        state.Fields.Add(new CreatedField(Guid.NewGuid(), "cr123_category", "cr123_sortorder", "Sort Order", "Integer", DateTime.UtcNow));

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task TableExists_ReturnsSuggestions()
    {
        var sessionId = "plan-gen-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.PlanGenerateReferenceDataAsync("cr123_category", 3, CancellationToken.None);

        Assert.Equal("cr123_category", result.TableLogicalName);
        Assert.Equal(3, result.RecordCount);
        Assert.Equal(3, result.SuggestedRecords.Count);
        Assert.Contains(result.Notes, n => n.Contains("Generated 3 suggested records"));
    }

    [Fact]
    public async Task TableNotFound_ReturnsNote()
    {
        var sessionId = "plan-gen-test-2";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.PlanGenerateReferenceDataAsync("cr123_nonexistent", 5, CancellationToken.None);

        Assert.Equal(0, result.RecordCount);
        Assert.Empty(result.SuggestedRecords);
        Assert.Contains(result.Notes, n => n.Contains("not found in project state"));
    }

    [Fact]
    public async Task DefaultRecordCount_UsedWhenZero()
    {
        var sessionId = "plan-gen-test-3";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.PlanGenerateReferenceDataAsync("cr123_category", 0, CancellationToken.None);

        Assert.Equal(5, result.RecordCount);
        Assert.Equal(5, result.SuggestedRecords.Count);
    }
}
