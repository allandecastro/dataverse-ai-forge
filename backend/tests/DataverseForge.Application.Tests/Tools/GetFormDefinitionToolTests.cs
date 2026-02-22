using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetFormDefinitionToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task GetFormDefinition_DelegatesToDataverseClient()
    {
        var formId = Guid.NewGuid();
        var expected = new FormDefinition(formId, "Main Form", "main", "<form><tabs></tabs></form>");

        _mockClient
            .Setup(c => c.GetFormDefinitionAsync(formId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetFormDefinitionTool(_mockClient.Object);
        var result = await tool.GetFormDefinitionAsync(formId.ToString(), CancellationToken.None);

        Assert.Equal("Main Form", result.Name);
        Assert.Equal("main", result.FormType);
        Assert.Contains("<form>", result.FormXml);
        _mockClient.Verify(c => c.GetFormDefinitionAsync(formId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
