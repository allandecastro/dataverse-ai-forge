using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GenerateErdDocToolTests
{
    private readonly Mock<IProjectStateStore> _mockStateStore = new();
    private readonly Mock<ISessionContext> _mockSession = new();

    private GenerateErdDocTool CreateTool() =>
        new(_mockStateStore.Object, _mockSession.Object);

    [Fact]
    public async Task GenerateAsync_NoTables_ReturnsMessage()
    {
        var sessionId = "erd-doc-1";
        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectState { ProjectId = sessionId });

        var tool = CreateTool();
        var result = await tool.GenerateErdDocAsync(true, CancellationToken.None);

        Assert.Contains("No tables", result);
    }

    [Fact]
    public async Task GenerateAsync_WithTables_ReturnsMermaidSyntax()
    {
        var sessionId = "erd-doc-2";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            Tables =
            [
                new CreatedTable(Guid.NewGuid(), "cr_project", "Project", DateTime.UtcNow),
                new CreatedTable(Guid.NewGuid(), "cr_task", "Task", DateTime.UtcNow)
            ],
            Relationships =
            [
                new CreatedRelationship(Guid.NewGuid(), "cr_project_tasks", "cr_project", "cr_task", "OneToMany", DateTime.UtcNow)
            ]
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateErdDocAsync(false, CancellationToken.None);

        Assert.Contains("erDiagram", result);
        Assert.Contains("cr_project", result);
        Assert.Contains("cr_task", result);
    }

    [Fact]
    public async Task GenerateAsync_IncludeFields_ShowsFieldsInDiagram()
    {
        var sessionId = "erd-doc-3";
        var state = new ProjectState
        {
            ProjectId = sessionId,
            Tables = [new CreatedTable(Guid.NewGuid(), "cr_project", "Project", DateTime.UtcNow)],
            Fields = [new CreatedField(Guid.NewGuid(), "cr_project", "cr_name", "Name", "StringType", DateTime.UtcNow)]
        };

        _mockSession.Setup(s => s.SessionId).Returns(sessionId);
        _mockStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var tool = CreateTool();
        var result = await tool.GenerateErdDocAsync(true, CancellationToken.None);

        Assert.Contains("cr_name", result);
        Assert.Contains("StringType", result);
    }
}
