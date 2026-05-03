using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateAlternateKeyToolTests
{
    private static (PlanCreateAlternateKeyTool tool, Dictionary<string, ProjectState> store) NewTool(
        ColumnInfo[]? columns = null,
        IReadOnlyList<AlternateKeyInfo>? existingKeys = null)
    {
        var stored = new Dictionary<string, ProjectState>();
        var mockStore = new Mock<IProjectStateStore>();
        mockStore
            .Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        mockStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);

        var mockCtx = new Mock<ISessionContext>();
        mockCtx.SetupGet(c => c.SessionId).Returns("test-session");

        var defaultColumns = columns ?? new[]
        {
            new ColumnInfo("cr123_externalid", "External ID", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_source", "Source", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_region", "Region", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_a", "A", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_b", "B", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_c", "C", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_d", "D", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_e", "E", "StringType", true, "None", 100, true),
            new ColumnInfo("cr123_f", "F", "StringType", true, "None", 100, true),
        };

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTableDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableDetails(
                "cr123_project", "Project", "cr123_Project", true, false, "UserOwned",
                "cr123_projectid", "cr123_name",
                defaultColumns, Array.Empty<RelationshipInfo>(), Array.Empty<AlternateKeyInfo>()));
        mockClient
            .Setup(c => c.GetAlternateKeysAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingKeys ?? Array.Empty<AlternateKeyInfo>());

        return (new PlanCreateAlternateKeyTool(mockStore.Object, mockCtx.Object, mockClient.Object), stored);
    }

    [Fact]
    public async Task PlanAlternateKey_ValidInput_ReturnsValidPlan()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "External Project ID",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_externalid",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.StartsWith("create_alt_key_", plan.PlanId);
        Assert.Equal("cr123_project", plan.TableLogicalName);
        Assert.Equal("cr123_externalprojectid", plan.KeyLogicalName);
        Assert.Single(plan.KeyAttributes);
        Assert.Equal("cr123_externalid", plan.KeyAttributes[0]);
        Assert.Contains("KeyAttributes", plan.WebApiPayloadJson);
        Assert.True(store["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanAlternateKey_MultipleAttributes_ParsesCorrectly()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "Composite Key",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_externalid, cr123_source, cr123_region",
            CancellationToken.None);

        Assert.Equal(3, plan.KeyAttributes.Count);
        Assert.Equal("cr123_externalid", plan.KeyAttributes[0]);
        Assert.Equal("cr123_source", plan.KeyAttributes[1]);
        Assert.Equal("cr123_region", plan.KeyAttributes[2]);
    }

    [Fact]
    public async Task PlanAlternateKey_TooManyAttributes_IsRejected()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "Big Key",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_a,cr123_b,cr123_c,cr123_d,cr123_e,cr123_f",
            CancellationToken.None);

        Assert.StartsWith("rejected_alt_key_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("at most 5"));
    }

    [Fact]
    public async Task PlanAlternateKey_MissingAttribute_IsRejected()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "Bad Key",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_doesnotexist",
            CancellationToken.None);

        Assert.StartsWith("rejected_alt_key_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("do NOT exist"));
    }

    [Fact]
    public async Task PlanAlternateKey_DuplicateAttributeSet_WarnsButPlans()
    {
        // Existing key already covers cr123_externalid — surface a DUPLICATE warning
        // but still produce a plan (user's call whether to skip).
        var (tool, _) = NewTool(existingKeys: new[]
        {
            new AlternateKeyInfo("cr123_oldkey", "Old Key", new[] { "cr123_externalid" }, Guid.NewGuid()),
        });

        var plan = await tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "External ID 2",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_externalid",
            CancellationToken.None);

        Assert.StartsWith("create_alt_key_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("DUPLICATE"));
    }

    [Fact]
    public async Task PlanAlternateKey_RiskLevel_IsYellow()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "External ID",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_externalid",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
