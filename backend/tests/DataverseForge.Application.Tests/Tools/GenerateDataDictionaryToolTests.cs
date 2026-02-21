using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GenerateDataDictionaryToolTests
{
    private readonly Mock<IProjectStateStore> _mockStateStore = new();
    private readonly Mock<ISessionContext> _mockSession = new();

    private GenerateDataDictionaryTool CreateTool() =>
        new(_mockStateStore.Object, _mockSession.Object);

    [Fact]
    public async Task GenerateAsync_NoTables_ReturnsEmptyMessage()
    {
        var sessionId = "dd-1";
        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectState { ProjectId = sessionId });

        var tool = CreateTool();
        var result = await tool.GenerateDataDictionaryAsync(true, CancellationToken.None);

        Assert.Contains("No tables", result);
    }

    [Fact]
    public async Task GenerateAsync_WithTables_ReturnsMarkdownWithTableHeaders()
    {
        var sessionId = "dd-2";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            SolutionDisplayName = "Test Solution",
            SolutionUniqueName = "testsolution",
            PublisherDisplayName = "Test Publisher",
            PublisherPrefix = "cr",
            Tables = [new CreatedTable(Guid.NewGuid(), "cr_project", "Project", DateTime.UtcNow)],
            Fields =
            [
                new CreatedField(Guid.NewGuid(), "cr_project", "cr_name", "Name", "StringType", DateTime.UtcNow),
                new CreatedField(Guid.NewGuid(), "cr_project", "cr_status", "Status", "PicklistType", DateTime.UtcNow)
            ]
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateDataDictionaryAsync(false, CancellationToken.None);

        Assert.Contains("# Data Dictionary", result);
        Assert.Contains("Project", result);
        Assert.Contains("`cr_project`", result);
        Assert.Contains("cr_name", result);
        Assert.Contains("StringType", result);
    }

    [Fact]
    public async Task GenerateAsync_WithRelationships_IncludesRelationshipSection()
    {
        var sessionId = "dd-3";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            Tables = [new CreatedTable(Guid.NewGuid(), "cr_project", "Project", DateTime.UtcNow)],
            Relationships =
            [
                new CreatedRelationship(Guid.NewGuid(), "cr_project_tasks", "cr_project", "cr_task", "OneToMany", DateTime.UtcNow)
            ]
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateDataDictionaryAsync(true, CancellationToken.None);

        Assert.Contains("## Relationships", result);
        Assert.Contains("cr_project_tasks", result);
        Assert.Contains("OneToMany", result);
    }

    [Fact]
    public async Task GenerateAsync_WithoutRelationships_OmitsRelationshipSection()
    {
        var sessionId = "dd-4";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            Tables = [new CreatedTable(Guid.NewGuid(), "cr_project", "Project", DateTime.UtcNow)]
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateDataDictionaryAsync(true, CancellationToken.None);

        Assert.DoesNotContain("## Relationships", result);
    }
}
