using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetFormsListToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task GetFormsList_DelegatesToDataverseClient()
    {
        var expected = new List<FormInfo>
        {
            new(Guid.NewGuid(), "Main Form", "main", "Default main form", true),
            new(Guid.NewGuid(), "Quick Create", "quickCreate", "Quick create form", false)
        };

        _mockClient
            .Setup(c => c.GetFormsListAsync("cr123_workorder", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetFormsListTool(_mockClient.Object);
        var result = await tool.GetFormsListAsync("cr123_workorder", CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Main Form", result[0].Name);
        _mockClient.Verify(c => c.GetFormsListAsync("cr123_workorder", It.IsAny<CancellationToken>()), Times.Once);
    }
}
