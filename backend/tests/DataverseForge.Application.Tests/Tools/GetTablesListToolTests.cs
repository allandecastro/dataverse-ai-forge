using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetTablesListToolTests
{
    [Fact]
    public async Task GetTablesListAsync_DelegatesAndSerializes()
    {
        var expected = new List<TableInfo>
        {
            new("account", "Account", "Account", false, false, "UserOwned",
                "accountid", "name", true, true, false)
        }.AsReadOnly();

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTablesListAsync("custom", null, "account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetTablesListTool(mockClient.Object, NullLogger<GetTablesListTool>.Instance);

        var result = await tool.GetTablesListAsync("custom", "", "account", CancellationToken.None);

        Assert.Contains("account", result);
        Assert.Contains("Account", result);
        mockClient.Verify(c => c.GetTablesListAsync("custom", null, "account", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTablesListAsync_ReturnsErrorString_WhenClientThrows()
    {
        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTablesListAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("400 Bad Request"));

        var tool = new GetTablesListTool(mockClient.Object, NullLogger<GetTablesListTool>.Instance);

        var result = await tool.GetTablesListAsync("solution", "FeedbackTraining", "", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("400 Bad Request", result);
    }
}
