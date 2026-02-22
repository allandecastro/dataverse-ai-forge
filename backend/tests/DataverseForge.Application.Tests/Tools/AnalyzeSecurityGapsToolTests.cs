using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class AnalyzeSecurityGapsToolTests
{
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    [Fact]
    public async Task AnalyzeSecurityGaps_DetectsPiiFields()
    {
        var sessionId = "analyze-1";
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        var state = new ProjectState { ProjectId = sessionId };
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "cr123_employee", "Employee", DateTime.UtcNow));
        state.Fields.Add(new CreatedField(Guid.NewGuid(), "cr123_employee", "cr123_email", "Email", "String", DateTime.UtcNow));
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new AnalyzeSecurityGapsTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.AnalyzeSecurityGapsAsync(CancellationToken.None);

        Assert.Single(result.PiiFieldsWithoutSecurity);
        Assert.Equal("cr123_email", result.PiiFieldsWithoutSecurity[0].FieldLogicalName);
        Assert.Equal("Personal", result.PiiFieldsWithoutSecurity[0].PiiCategory);
    }

    [Fact]
    public async Task AnalyzeSecurityGaps_DetectsTablesWithoutRoles()
    {
        var sessionId = "analyze-2";
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        var state = new ProjectState { ProjectId = sessionId };
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "cr123_employee", "Employee", DateTime.UtcNow));
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new AnalyzeSecurityGapsTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.AnalyzeSecurityGapsAsync(CancellationToken.None);

        Assert.Single(result.TablesWithoutCustomRoles);
        Assert.Equal("cr123_employee", result.TablesWithoutCustomRoles[0]);
    }

    [Fact]
    public async Task AnalyzeSecurityGaps_NoFieldsNoPii()
    {
        var sessionId = "analyze-3";
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        var state = new ProjectState { ProjectId = sessionId };
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new AnalyzeSecurityGapsTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.AnalyzeSecurityGapsAsync(CancellationToken.None);

        Assert.Empty(result.PiiFieldsWithoutSecurity);
    }

    [Fact]
    public async Task AnalyzeSecurityGaps_RecommendsRolesWhenNoneExist()
    {
        var sessionId = "analyze-4";
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        var state = new ProjectState { ProjectId = sessionId };
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "cr123_employee", "Employee", DateTime.UtcNow));
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var tool = new AnalyzeSecurityGapsTool(_mockProjectStateStore.Object, _mockSessionContext.Object);
        var result = await tool.AnalyzeSecurityGapsAsync(CancellationToken.None);

        Assert.Empty(state.SecurityRoles);
        Assert.Contains(result.Recommendations, r =>
            r.Contains("No custom security roles") && r.Contains("Viewer"));
    }
}
