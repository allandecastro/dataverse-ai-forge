using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetEnvironmentInfoToolTests
{
    [Fact]
    public async Task GetEnvironmentInfoAsync_DelegatesToDataverseClient()
    {
        var expected = new EnvironmentInfo(
            OrganizationName: "Contoso",
            EnvironmentUrl: "https://contoso.crm.dynamics.com",
            BaseLanguageLcid: 1033,
            DataverseVersion: "9.2.24041.00170",
            Region: "NAM",
            BaseCurrencyCode: "USD",
            IsAuditEnabled: true);

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetEnvironmentInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var mockStore = new Mock<IProjectStateStore>();
        mockStore.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectState());
        var mockContext = new Mock<ISessionContext>();
        mockContext.SetupGet(c => c.SessionId).Returns("test-session");

        var tool = new GetEnvironmentInfoTool(
            mockClient.Object,
            mockStore.Object,
            mockContext.Object,
            NullLogger<GetEnvironmentInfoTool>.Instance);

        var result = await tool.GetEnvironmentInfoAsync(CancellationToken.None);

        Assert.Contains("Contoso", result);
        Assert.Contains("contoso.crm.dynamics.com", result);
        mockClient.Verify(c => c.GetEnvironmentInfoAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEnvironmentInfoAsync_ReturnsErrorString_WhenClientThrows()
    {
        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetEnvironmentInfoAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401 Unauthorized"));

        var mockStore = new Mock<IProjectStateStore>();
        var mockContext = new Mock<ISessionContext>();
        mockContext.SetupGet(c => c.SessionId).Returns("test-session");

        var tool = new GetEnvironmentInfoTool(
            mockClient.Object,
            mockStore.Object,
            mockContext.Object,
            NullLogger<GetEnvironmentInfoTool>.Instance);

        var result = await tool.GetEnvironmentInfoAsync(CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("401 Unauthorized", result);
    }
}
