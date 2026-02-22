using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyBpfCreationToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();

    [Fact]
    public async Task VerifyBpfCreation_Exists_ReturnPass()
    {
        _mockDataverseClient
            .Setup(c => c.GetBpfMetadataAsync("WorkOrderProcess", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BpfMetadata(Guid.NewGuid(), "WorkOrderProcess", "cr123_workorder", "4", 1, 2));

        var tool = new VerifyBpfCreationTool(_mockDataverseClient.Object);
        var result = await tool.VerifyBpfCreationAsync("WorkOrderProcess", "cr123_workorder", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.Matches);
        Assert.Empty(result.Discrepancies);
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifyBpfCreation_NotFound_ReturnFail()
    {
        _mockDataverseClient
            .Setup(c => c.GetBpfMetadataAsync("NonExistent", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = new VerifyBpfCreationTool(_mockDataverseClient.Object);
        var result = await tool.VerifyBpfCreationAsync("NonExistent", "cr123_workorder", CancellationToken.None);

        Assert.False(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual == "NOT FOUND");
    }

    [Fact]
    public async Task VerifyBpfCreation_PrimaryTableMismatch_ReturnFail()
    {
        _mockDataverseClient
            .Setup(c => c.GetBpfMetadataAsync("WorkOrderProcess", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BpfMetadata(Guid.NewGuid(), "WorkOrderProcess", "cr123_case", "4", 1, 2));

        var tool = new VerifyBpfCreationTool(_mockDataverseClient.Object);
        var result = await tool.VerifyBpfCreationAsync("WorkOrderProcess", "cr123_workorder", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d =>
            d.Property == "PrimaryTable" &&
            d.Expected == "cr123_workorder" &&
            d.Actual == "cr123_case");
    }
}
