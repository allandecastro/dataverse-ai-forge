using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyRelationshipCreationToolTests
{
    private readonly Mock<IDataverseClient> _mockClient = new();

    [Fact]
    public async Task VerifyAsync_RelationshipExists_AllMatch_ReturnsPass()
    {
        _mockClient
            .Setup(c => c.GetRelationshipMetadataAsync("cr_project_tasks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipMetadata(
                "cr_project_tasks", "cr_project", "cr_projectid", "cr_task", "cr_projectid",
                CascadeAssign: "NoCascade", CascadeDelete: "RemoveLink",
                CascadeReparent: "NoCascade", CascadeMerge: "NoCascade",
                CascadeShare: "NoCascade", CascadeUnshare: "NoCascade"));

        var tool = new VerifyRelationshipCreationTool(_mockClient.Object);
        var result = await tool.VerifyRelationshipCreationAsync(
            "cr_project_tasks", "cr_project", "cr_task", "RemoveLink", "NoCascade",
            CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.Matches);
        Assert.Empty(result.Discrepancies);
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_CascadeDeleteMismatch_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetRelationshipMetadataAsync("cr_project_tasks", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipMetadata(
                "cr_project_tasks", "cr_project", "cr_projectid", "cr_task", "cr_projectid",
                CascadeAssign: "NoCascade", CascadeDelete: "Cascade",
                CascadeReparent: "NoCascade", CascadeMerge: "NoCascade",
                CascadeShare: "NoCascade", CascadeUnshare: "NoCascade"));

        var tool = new VerifyRelationshipCreationTool(_mockClient.Object);
        var result = await tool.VerifyRelationshipCreationAsync(
            "cr_project_tasks", "cr_project", "cr_task", "RemoveLink", "NoCascade",
            CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "CascadeDelete" && d.Expected == "RemoveLink" && d.Actual == "Cascade");
    }

    [Fact]
    public async Task VerifyAsync_RelationshipNotFound_ReturnsFail()
    {
        _mockClient
            .Setup(c => c.GetRelationshipMetadataAsync("cr_missing_rel", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = new VerifyRelationshipCreationTool(_mockClient.Object);
        var result = await tool.VerifyRelationshipCreationAsync(
            "cr_missing_rel", "cr_parent", "cr_child", "RemoveLink", "NoCascade",
            CancellationToken.None);

        Assert.False(result.Exists);
        Assert.Equal("FAIL", result.Verdict);
    }

    [Fact]
    public async Task VerifyAsync_EntityMismatch_ReportsDiscrepancy()
    {
        _mockClient
            .Setup(c => c.GetRelationshipMetadataAsync("cr_rel", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipMetadata(
                "cr_rel", "cr_wrong_parent", "cr_id", "cr_child", "cr_parentid",
                CascadeAssign: "NoCascade", CascadeDelete: "RemoveLink",
                CascadeReparent: "NoCascade", CascadeMerge: "NoCascade",
                CascadeShare: "NoCascade", CascadeUnshare: "NoCascade"));

        var tool = new VerifyRelationshipCreationTool(_mockClient.Object);
        var result = await tool.VerifyRelationshipCreationAsync(
            "cr_rel", "cr_parent", "cr_child", "RemoveLink", "NoCascade",
            CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "ReferencedEntity");
    }
}
