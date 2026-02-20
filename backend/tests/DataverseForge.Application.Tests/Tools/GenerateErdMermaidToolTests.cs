using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GenerateErdMermaidToolTests
{
    [Fact]
    public async Task GenerateErdMermaidAsync_EmptyState_ReturnsMessage()
    {
        // Arrange
        var state = new ProjectState();
        var mockStore = new Mock<IProjectStateStore>();
        mockStore
            .Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var mockSession = new Mock<ISessionContext>();
        mockSession.Setup(s => s.SessionId).Returns("test-session");

        var tool = new GenerateErdMermaidTool(mockStore.Object, mockSession.Object);

        // Act
        var result = await tool.GenerateErdMermaidAsync(null, false, CancellationToken.None);

        // Assert
        Assert.Contains("No tables have been designed yet", result);
    }

    [Fact]
    public async Task GenerateErdMermaidAsync_WithTablesAndRelationships_ReturnsMermaid()
    {
        // Arrange
        var state = new ProjectState
        {
            Tables =
            [
                new(Guid.NewGuid(), "cr123_order", "Order", DateTime.UtcNow),
                new(Guid.NewGuid(), "cr123_orderline", "Order Line", DateTime.UtcNow)
            ],
            Fields =
            [
                new(Guid.NewGuid(), "cr123_order", "cr123_name", "Name", "StringType", DateTime.UtcNow),
                new(Guid.NewGuid(), "cr123_orderline", "cr123_quantity", "Quantity", "IntegerType", DateTime.UtcNow)
            ],
            Relationships =
            [
                new(Guid.NewGuid(), "cr123_order_orderlines", "cr123_order", "cr123_orderline", "OneToMany", DateTime.UtcNow)
            ]
        };

        var mockStore = new Mock<IProjectStateStore>();
        mockStore
            .Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var mockSession = new Mock<ISessionContext>();
        mockSession.Setup(s => s.SessionId).Returns("test-session");

        var tool = new GenerateErdMermaidTool(mockStore.Object, mockSession.Object);

        // Act
        var result = await tool.GenerateErdMermaidAsync(null, true, CancellationToken.None);

        // Assert
        Assert.Contains("erDiagram", result);
        Assert.Contains("cr123_order", result);
        Assert.Contains("cr123_orderline", result);
        Assert.Contains("||--o{", result);
        Assert.Contains("cr123_name", result);
        Assert.Contains("cr123_quantity", result);
    }
}
