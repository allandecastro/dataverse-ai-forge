using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class RunFullValidationToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();
    private readonly Mock<IProjectStateStore> _mockStateStore = new();
    private readonly Mock<ISessionContext> _mockSession = new();

    private RunFullValidationTool CreateTool() =>
        new(_mockClient.Object, _mockStateStore.Object, _mockSession.Object);

    [Fact]
    public async Task RunAsync_AllComponentsExist_ReturnsPass()
    {
        var sessionId = "full-val-1";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            Tables = [new CreatedTable(Guid.NewGuid(), "cr_project", "Project", DateTime.UtcNow)],
            Fields = [new CreatedField(Guid.NewGuid(), "cr_project", "cr_name", "Name", "StringType", DateTime.UtcNow)],
            Relationships = [new CreatedRelationship(Guid.NewGuid(), "cr_project_tasks", "cr_project", "cr_task", "OneToMany", DateTime.UtcNow)]
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_project", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableMetadata("cr_project", "Project", "cr_project", "UserOwned", false, false, false, false));
        _mockClient
            .Setup(c => c.GetFieldMetadataAsync("cr_project", "cr_name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMetadata("cr_name", "Name", "StringType", "None", 200, true, false));
        _mockClient
            .Setup(c => c.GetRelationshipMetadataAsync("cr_project_tasks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipMetadata("cr_project_tasks", "cr_project", "cr_projectid", "cr_task", "cr_projectid",
                "NoCascade", "RemoveLink", "NoCascade", "NoCascade", "NoCascade", "NoCascade"));

        var tool = CreateTool();
        var result = await tool.RunFullValidationAsync(CancellationToken.None);

        Assert.Equal("PASS", result.OverallVerdict);
        Assert.Equal(1, result.Tables.Passed);
        Assert.Equal(1, result.Fields.Passed);
        Assert.Equal(1, result.Relationships.Passed);
        Assert.Contains("3/3", result.Summary);
    }

    [Fact]
    public async Task RunAsync_SomeComponentsMissing_ReturnsPartial()
    {
        var sessionId = "full-val-2";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            Tables =
            [
                new CreatedTable(Guid.NewGuid(), "cr_project", "Project", DateTime.UtcNow),
                new CreatedTable(Guid.NewGuid(), "cr_missing", "Missing", DateTime.UtcNow)
            ],
            Fields = [],
            Relationships = []
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_project", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableMetadata("cr_project", "Project", "cr_project", "UserOwned", false, false, false, false));
        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = CreateTool();
        var result = await tool.RunFullValidationAsync(CancellationToken.None);

        Assert.Equal("PARTIAL", result.OverallVerdict);
        Assert.Equal(1, result.Tables.Passed);
        Assert.Equal(1, result.Tables.Failed);
    }

    [Fact]
    public async Task RunAsync_EmptyState_ReturnsPassWithZeroComponents()
    {
        var sessionId = "full-val-3";
        var state = new ProjectState { ProjectId = sessionId };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.RunFullValidationAsync(CancellationToken.None);

        Assert.Equal("PASS", result.OverallVerdict);
        Assert.Equal(0, result.Tables.Total);
        Assert.Equal(0, result.Fields.Total);
        Assert.Equal(0, result.Relationships.Total);
        Assert.Contains("0/0", result.Summary);
    }

    [Fact]
    public async Task RunAsync_AllComponentsFail_ReturnsFail()
    {
        var sessionId = "full-val-4";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            Tables = [new CreatedTable(Guid.NewGuid(), "cr_gone", "Gone", DateTime.UtcNow)],
            Fields = [new CreatedField(Guid.NewGuid(), "cr_gone", "cr_field", "Field", "StringType", DateTime.UtcNow)],
            Relationships = []
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_gone", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));
        _mockClient
            .Setup(c => c.GetFieldMetadataAsync("cr_gone", "cr_field", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = CreateTool();
        var result = await tool.RunFullValidationAsync(CancellationToken.None);

        Assert.Equal("FAIL", result.OverallVerdict);
        Assert.Equal(0, result.Tables.Passed);
        Assert.Equal(2, result.Tables.Failed + result.Fields.Failed);
    }
}
