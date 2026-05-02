using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Agents;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;
using Moq;

namespace DataverseForge.Application.Tests.Orchestration;

public class AgentOrchestratorTests
{
    private readonly Mock<IAgent> _mockArchitect = new();
    private readonly Mock<IAgent> _mockBa = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ILogger<AgentOrchestrator>> _mockLogger = new();

    public AgentOrchestratorTests()
    {
        _mockArchitect.Setup(a => a.Name).Returns(AgentNames.SolutionArchitect);
        _mockBa.Setup(a => a.Name).Returns(AgentNames.BusinessAnalyst);
    }

    private AgentOrchestrator CreateOrchestrator() =>
        new([_mockArchitect.Object, _mockBa.Object],
            _mockProjectStateStore.Object, _mockLogger.Object);

    [Fact]
    public async Task ChatAsync_NewSession_RoutesToSolutionArchitect()
    {
        var sessionId = "orch-test-1";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.SolutionArchitect };
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var expectedResponse = new ChatResponse(sessionId, "Hello!", state);
        _mockArchitect
            .Setup(a => a.ChatAsync(sessionId, "Hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.ChatAsync(sessionId, "Hello", CancellationToken.None);

        Assert.Equal("Hello!", result.Response);
        _mockArchitect.Verify(a => a.ChatAsync(sessionId, "Hello", It.IsAny<CancellationToken>()), Times.Once);
        _mockBa.Verify(a => a.ChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_ActiveAgentIsBusinessAnalyst_RoutesToBa()
    {
        var sessionId = "orch-test-2";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = AgentNames.BusinessAnalyst };
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var expectedResponse = new ChatResponse(sessionId, "Tell me more.", state);
        _mockBa
            .Setup(a => a.ChatAsync(sessionId, "I need a feedback module", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.ChatAsync(sessionId, "I need a feedback module", CancellationToken.None);

        Assert.Equal("Tell me more.", result.Response);
        _mockBa.Verify(a => a.ChatAsync(sessionId, "I need a feedback module", It.IsAny<CancellationToken>()), Times.Once);
        _mockArchitect.Verify(a => a.ChatAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChatAsync_UnknownActiveAgent_FallsBackToArchitect()
    {
        var sessionId = "orch-test-3";
        var state = new ProjectState { ProjectId = sessionId, ActiveAgentName = "NonExistentAgent" };
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var expectedResponse = new ChatResponse(sessionId, "I'm here to help.", state);
        _mockArchitect
            .Setup(a => a.ChatAsync(sessionId, "Hello", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.ChatAsync(sessionId, "Hello", CancellationToken.None);

        Assert.Equal("I'm here to help.", result.Response);
        _mockArchitect.Verify(a => a.ChatAsync(sessionId, "Hello", It.IsAny<CancellationToken>()), Times.Once);
        // Verify state was corrected
        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.Is<ProjectState>(ps => ps.ActiveAgentName == AgentNames.SolutionArchitect), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProjectStateAsync_DelegatesToStore()
    {
        var sessionId = "orch-test-4";
        var expectedState = new ProjectState { ProjectId = sessionId, ProjectName = "Test" };
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedState);

        var orchestrator = CreateOrchestrator();
        var result = await orchestrator.GetProjectStateAsync(sessionId, CancellationToken.None);

        Assert.Equal("Test", result.ProjectName);
    }
}
