using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetEnvironmentInfoToolTests
{
    [Fact]
    public async Task GetEnvironmentInfoAsync_DelegatesToDataverseClient()
    {
        // Arrange
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

        var tool = new GetEnvironmentInfoTool(mockClient.Object, mockStore.Object, mockContext.Object);

        // Act
        var result = await tool.GetEnvironmentInfoAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expected, result);
        mockClient.Verify(c => c.GetEnvironmentInfoAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
