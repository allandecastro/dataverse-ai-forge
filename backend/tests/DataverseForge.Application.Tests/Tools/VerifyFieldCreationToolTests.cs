using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyFieldCreationToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task VerifyAsync_FieldExists_AllMatch_ReturnsPass()
    {
        _mockClient
            .Setup(c => c.GetFieldMetadataAsync("cr_project", "cr_name", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMetadata(Guid.NewGuid(),
                "cr_name", "Name", "StringType", "ApplicationRequired",
                MaxLength: 200, IsSearchable: true, IsAuditEnabled: true));

        var tool = new VerifyFieldCreationTool(_mockClient.Object);
        var result = await tool.VerifyFieldCreationAsync(
            "cr_project", "cr_name", "StringType", "ApplicationRequired", true,
            CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.TypeMatches);
        Assert.True(result.ConstraintsMatch);
        Assert.Empty(result.Discrepancies);
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_TypeMismatch_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetFieldMetadataAsync("cr_project", "cr_count", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMetadata(Guid.NewGuid(),
                "cr_count", "Count", "StringType", "None",
                MaxLength: null, IsSearchable: false, IsAuditEnabled: false));

        var tool = new VerifyFieldCreationTool(_mockClient.Object);
        var result = await tool.VerifyFieldCreationAsync(
            "cr_project", "cr_count", "IntegerType", "None", false,
            CancellationToken.None);

        Assert.True(result.Exists);
        Assert.False(result.TypeMatches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "AttributeType");
    }

    [Fact]
    public async Task VerifyAsync_RequiredLevelMismatch_ConstraintsDoNotMatch()
    {
        _mockClient
            .Setup(c => c.GetFieldMetadataAsync("cr_project", "cr_status", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMetadata(Guid.NewGuid(),
                "cr_status", "Status", "PicklistType", "None",
                MaxLength: null, IsSearchable: true, IsAuditEnabled: false));

        var tool = new VerifyFieldCreationTool(_mockClient.Object);
        var result = await tool.VerifyFieldCreationAsync(
            "cr_project", "cr_status", "PicklistType", "ApplicationRequired", true,
            CancellationToken.None);

        Assert.True(result.TypeMatches);
        Assert.False(result.ConstraintsMatch);
        Assert.Contains(result.Discrepancies, d => d.Property == "RequiredLevel");
    }

    [Fact]
    public async Task VerifyAsync_FieldNotFound_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetFieldMetadataAsync("cr_project", "cr_missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = new VerifyFieldCreationTool(_mockClient.Object);
        var result = await tool.VerifyFieldCreationAsync(
            "cr_project", "cr_missing", "StringType", "None", false,
            CancellationToken.None);

        Assert.False(result.Exists);
        Assert.False(result.TypeMatches);
        Assert.False(result.ConstraintsMatch);
        Assert.Equal("FAIL", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_SearchableMismatch_ReportsDiscrepancy()
    {
        _mockClient
            .Setup(c => c.GetFieldMetadataAsync("cr_project", "cr_desc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMetadata(Guid.NewGuid(),
                "cr_desc", "Description", "MemoType", "None",
                MaxLength: 2000, IsSearchable: false, IsAuditEnabled: false));

        var tool = new VerifyFieldCreationTool(_mockClient.Object);
        var result = await tool.VerifyFieldCreationAsync(
            "cr_project", "cr_desc", "MemoType", "None", true,
            CancellationToken.None);

        Assert.True(result.TypeMatches);
        Assert.False(result.ConstraintsMatch);
        Assert.Contains(result.Discrepancies, d => d.Property == "IsSearchable");
    }
}
