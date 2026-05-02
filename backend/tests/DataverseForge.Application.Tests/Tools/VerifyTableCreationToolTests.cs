using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyTableCreationToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task VerifyAsync_TableExists_AllMatch_ReturnsPass()
    {
        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_project", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableMetadata(
                Guid.NewGuid(), "cr_project", "Project", "cr_project", "UserOwned",
                HasNotes: true, HasActivities: false, ChangeTrackingEnabled: true, IsAuditEnabled: true));

        var tool = new VerifyTableCreationTool(_mockClient.Object);
        var result = await tool.VerifyTableCreationAsync(
            "cr_project", "Project", "UserOwned",
            expectedHasNotes: true, expectedHasActivities: false, expectedChangeTrackingEnabled: true,
            CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.Matches);
        Assert.Empty(result.Discrepancies);
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_TableExists_DisplayNameMismatch_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_project", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableMetadata(
                Guid.NewGuid(), "cr_project", "Wrong Name", "cr_project", "UserOwned",
                HasNotes: true, HasActivities: false, ChangeTrackingEnabled: true, IsAuditEnabled: true));

        var tool = new VerifyTableCreationTool(_mockClient.Object);
        var result = await tool.VerifyTableCreationAsync(
            "cr_project", "Project", "UserOwned",
            expectedHasNotes: true, expectedHasActivities: false, expectedChangeTrackingEnabled: true,
            CancellationToken.None);

        Assert.True(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "DisplayName");
    }

    [Fact]
    public async Task VerifyAsync_TableNotFound_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_missing", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = new VerifyTableCreationTool(_mockClient.Object);
        var result = await tool.VerifyTableCreationAsync(
            "cr_missing", "Missing", "UserOwned",
            expectedHasNotes: false, expectedHasActivities: false, expectedChangeTrackingEnabled: false,
            CancellationToken.None);

        Assert.False(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_OwnershipTypeMismatch_ReportsDiscrepancy()
    {
        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_item", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableMetadata(
                Guid.NewGuid(), "cr_item", "Item", "cr_item", "OrganizationOwned",
                HasNotes: false, HasActivities: false, ChangeTrackingEnabled: false, IsAuditEnabled: false));

        var tool = new VerifyTableCreationTool(_mockClient.Object);
        var result = await tool.VerifyTableCreationAsync(
            "cr_item", "Item", "UserOwned",
            expectedHasNotes: false, expectedHasActivities: false, expectedChangeTrackingEnabled: false,
            CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "OwnershipType" && d.Expected == "UserOwned" && d.Actual == "OrganizationOwned");
    }

    [Fact]
    public async Task VerifyAsync_MultipleDiscrepancies_ReportsAll()
    {
        _mockClient
            .Setup(c => c.GetTableMetadataAsync("cr_item", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableMetadata(
                Guid.NewGuid(), "cr_item", "Wrong", "cr_item", "OrganizationOwned",
                HasNotes: true, HasActivities: true, ChangeTrackingEnabled: false, IsAuditEnabled: false));

        var tool = new VerifyTableCreationTool(_mockClient.Object);
        var result = await tool.VerifyTableCreationAsync(
            "cr_item", "Item", "UserOwned",
            expectedHasNotes: false, expectedHasActivities: false, expectedChangeTrackingEnabled: true,
            CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.True(result.Discrepancies.Count >= 4);
    }
}
