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
    public async Task VerifyAsync_DirectMetadata404_FallsBackToEntityListing_ReturnsPass()
    {
        // Real-session failure: a 1:N relationship was created at T+0, but
        // RelationshipDefinitions(SchemaName='X') returned 404 at T+12s due
        // to metadata-cache lag. Meanwhile GetTableDetails on the referencing
        // entity already showed the relationship. The verifier must fall
        // back to that path so it doesn't false-FAIL on perfectly good
        // relationships.
        _mockClient
            .Setup(c => c.GetRelationshipMetadataAsync("andras_contact_gift", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404 Not Found"));

        _mockClient
            .Setup(c => c.GetTableDetailsAsync("andras_gift", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableDetails(
                LogicalName: "andras_gift",
                DisplayName: "Gift",
                SchemaName: "andras_Gift",
                IsCustom: true,
                IsManaged: false,
                OwnershipType: "UserOwned",
                PrimaryIdAttribute: "andras_giftid",
                PrimaryNameAttribute: "andras_giftname",
                Columns: Array.Empty<ColumnInfo>(),
                Relationships: new List<RelationshipInfo>
                {
                    new(
                        SchemaName: "andras_contact_gift",
                        RelationshipType: "OneToMany",
                        ReferencedEntity: "contact",
                        ReferencedAttribute: "contactid",
                        ReferencingEntity: "andras_gift",
                        ReferencingAttribute: "andras_contactid")
                },
                AlternateKeys: Array.Empty<AlternateKeyInfo>()));

        var tool = new VerifyRelationshipCreationTool(_mockClient.Object);
        var result = await tool.VerifyRelationshipCreationAsync(
            "andras_contact_gift", "contact", "andras_gift", "RemoveLink", "NoCascade",
            CancellationToken.None);

        // Fallback found it — verdict is PASS even though cascade config
        // wasn't checked (the fallback path doesn't carry that data).
        Assert.True(result.Exists);
        Assert.Equal("PASS", result.Verdict);
        Assert.Empty(result.Discrepancies);
    }

    [Fact]
    public async Task VerifyAsync_BothPathsFail_ReturnsNotFound()
    {
        // Both endpoints fail (e.g. real metadata that genuinely doesn't exist).
        _mockClient
            .Setup(c => c.GetRelationshipMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404"));
        _mockClient
            .Setup(c => c.GetTableDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404"));

        var tool = new VerifyRelationshipCreationTool(_mockClient.Object);
        var result = await tool.VerifyRelationshipCreationAsync(
            "cr_truly_missing", "cr_parent", "cr_child", "RemoveLink", "NoCascade",
            CancellationToken.None);

        Assert.False(result.Exists);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual == "NOT FOUND");
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
