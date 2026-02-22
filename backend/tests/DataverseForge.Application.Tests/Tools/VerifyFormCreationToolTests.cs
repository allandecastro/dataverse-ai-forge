using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyFormCreationToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task VerifyAsync_FormExists_AllMatch_ReturnsPass()
    {
        var formId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.GetFormDefinitionAsync(formId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormDefinition(formId, "WO Main Form", "main", "<form/>"));

        var tool = new VerifyFormCreationTool(_mockClient.Object);
        var result = await tool.VerifyFormCreationAsync(
            "cr123_workorder", formId.ToString(), "WO Main Form", "main", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.Matches);
        Assert.Empty(result.Discrepancies);
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_FormExists_NameMismatch_ReturnsFail()
    {
        var formId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.GetFormDefinitionAsync(formId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormDefinition(formId, "Wrong Name", "main", "<form/>"));

        var tool = new VerifyFormCreationTool(_mockClient.Object);
        var result = await tool.VerifyFormCreationAsync(
            "cr123_workorder", formId.ToString(), "WO Main Form", "main", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "Name");
    }

    [Fact]
    public async Task VerifyAsync_FormNotFound_ReturnsFail()
    {
        var formId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.GetFormDefinitionAsync(formId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = new VerifyFormCreationTool(_mockClient.Object);
        var result = await tool.VerifyFormCreationAsync(
            "cr123_workorder", formId.ToString(), "WO Main Form", "main", CancellationToken.None);

        Assert.False(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_FormTypeMismatch_ReportsDiscrepancy()
    {
        var formId = Guid.NewGuid();
        _mockClient
            .Setup(c => c.GetFormDefinitionAsync(formId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FormDefinition(formId, "Quick Create", "quickCreate", "<form/>"));

        var tool = new VerifyFormCreationTool(_mockClient.Object);
        var result = await tool.VerifyFormCreationAsync(
            "cr123_workorder", formId.ToString(), "Quick Create", "main", CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "FormType" && d.Expected == "main" && d.Actual == "quickCreate");
    }
}
