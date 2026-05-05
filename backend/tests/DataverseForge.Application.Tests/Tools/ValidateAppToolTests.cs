using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ValidateAppToolTests
{
    private readonly Mock<IDataverseClient> _client = new();

    [Fact]
    public async Task ValidateApp_DelegatesToDataverseClient_AndForwardsResult()
    {
        var appId = Guid.NewGuid();
        var expected = new AppValidationResult(appId, IsValid: false,
            new List<AppValidationIssue>
            {
                new("Error", "App is missing a sitemap.", null, null, 62),
            }.AsReadOnly());

        _client.Setup(c => c.ValidateAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new ValidateAppTool(_client.Object);
        var result = await tool.ValidateAppAsync(appId, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Single(result.Issues);
        Assert.Equal("App is missing a sitemap.", result.Issues[0].Message);
        _client.Verify(c => c.ValidateAppAsync(appId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateApp_ForwardsValidResult()
    {
        var appId = Guid.NewGuid();
        _client.Setup(c => c.ValidateAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppValidationResult(appId, IsValid: true, Array.Empty<AppValidationIssue>()));

        var tool = new ValidateAppTool(_client.Object);
        var result = await tool.ValidateAppAsync(appId, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Empty(result.Issues);
    }
}
