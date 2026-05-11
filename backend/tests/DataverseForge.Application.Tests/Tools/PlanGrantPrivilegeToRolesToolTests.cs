using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanGrantPrivilegeToRolesToolTests
{
    private static readonly Guid RoleAId = Guid.Parse("dddd1111-dddd-1111-dddd-111111111111");
    private static readonly Guid RoleBId = Guid.Parse("dddd2222-dddd-2222-dddd-222222222222");
    private static readonly Guid RoleCId = Guid.Parse("dddd3333-dddd-3333-dddd-333333333333");
    private static readonly Guid BuId = Guid.Parse("dddd4444-dddd-4444-dddd-444444444444");
    private static readonly Guid PrivReadId = Guid.Parse("dddd5555-dddd-5555-dddd-555555555555");

    private static (PlanGrantPrivilegeToRolesTool tool, Dictionary<string, ProjectState> store, Mock<IDataverseClient> mockClient) NewTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        var mockStore = new Mock<IProjectStateStore>();
        mockStore.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState(); stored[id] = s; }
                return s;
            });
        mockStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);

        var mockCtx = new Mock<ISessionContext>();
        mockCtx.SetupGet(c => c.SessionId).Returns("bulk-session");

        var mockClient = new Mock<IDataverseClient>();
        mockClient.Setup(c => c.GetPrivilegesForTableAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PrivilegeMetadata>
            {
                new(PrivReadId, "prvReadAccount", "Read", true, true, true, true),
            });

        // All three roles exist, unmanaged.
        foreach (var id in new[] { RoleAId, RoleBId, RoleCId })
        {
            var capturedId = id;
            mockClient.Setup(c => c.GetSecurityRoleAsync(capturedId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SecurityRoleDetails(
                    Info: new SecurityRoleInfo(capturedId, $"Role-{capturedId}", null, BuId, "Root", false, false, null, null, null),
                    ParentRoleId: null,
                    IsCustomizable: true));
        }

        return (new PlanGrantPrivilegeToRolesTool(mockStore.Object, mockCtx.Object, mockClient.Object), stored, mockClient);
    }

    [Fact]
    public async Task PlanBulkGrant_HappyPath_StagesPayload()
    {
        var (tool, store, _) = NewTool();
        var roleIds = $"[\"{RoleAId}\",\"{RoleBId}\",\"{RoleCId}\"]";

        var plan = await tool.PlanGrantPrivilegeToRolesAsync(
            roleIdsJson: roleIds,
            tableLogicalName: "account",
            operation: "Read",
            depth: "BU",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("grant_privilege_to_roles_", plan.PlanId);
        Assert.Equal(PrivReadId, plan.PrivilegeId);
        Assert.Equal("Local", plan.Depth);
        Assert.Equal(3, plan.TargetRoleIds.Count);
        Assert.Equal("orange", plan.RiskLevel);
        Assert.True(store["bulk-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanBulkGrant_DedupesAndDropsEmptyIds()
    {
        var (tool, _, _) = NewTool();
        // Duplicates + a Guid.Empty in the middle — both should be filtered.
        var roleIds = $"[\"{RoleAId}\",\"{RoleAId}\",\"{Guid.Empty}\",\"{RoleBId}\"]";

        var plan = await tool.PlanGrantPrivilegeToRolesAsync(
            roleIdsJson: roleIds,
            tableLogicalName: "account",
            operation: "Read",
            depth: "BU",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("grant_privilege_to_roles_", plan.PlanId);
        Assert.Equal(2, plan.TargetRoleIds.Count);
        Assert.Contains(RoleAId, plan.TargetRoleIds);
        Assert.Contains(RoleBId, plan.TargetRoleIds);
    }

    [Fact]
    public async Task PlanBulkGrant_EmptyAfterDedup_Rejects()
    {
        var (tool, _, _) = NewTool();
        var roleIds = $"[\"{Guid.Empty}\"]";

        var plan = await tool.PlanGrantPrivilegeToRolesAsync(
            roleIdsJson: roleIds,
            tableLogicalName: "account",
            operation: "Read",
            depth: "BU",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_bulk_grant_invalid_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("roleIds is empty"));
    }

    [Fact]
    public async Task PlanBulkGrant_ManagedRoleInList_Rejects()
    {
        var (tool, _, mockClient) = NewTool();
        // Replace RoleB with a managed role.
        mockClient.Setup(c => c.GetSecurityRoleAsync(RoleBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRoleDetails(
                Info: new SecurityRoleInfo(RoleBId, "Managed Role", null, BuId, "Root", true, false, null, null, null),
                ParentRoleId: null,
                IsCustomizable: false));

        var roleIds = $"[\"{RoleAId}\",\"{RoleBId}\"]";
        var plan = await tool.PlanGrantPrivilegeToRolesAsync(
            roleIdsJson: roleIds,
            tableLogicalName: "account",
            operation: "Read",
            depth: "BU",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_bulk_grant_managed_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("Managed Role"));
    }

    [Fact]
    public async Task PlanBulkGrant_MissingRole_Rejects()
    {
        var (tool, _, mockClient) = NewTool();
        var missingId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        mockClient.Setup(c => c.GetSecurityRoleAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SecurityRoleDetails?)null);

        var roleIds = $"[\"{RoleAId}\",\"{missingId}\"]";
        var plan = await tool.PlanGrantPrivilegeToRolesAsync(
            roleIdsJson: roleIds,
            tableLogicalName: "account",
            operation: "Read",
            depth: "BU",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_bulk_grant_invalid_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("do not exist"));
    }

    [Fact]
    public async Task PlanBulkGrant_BadJson_Rejects()
    {
        var (tool, _, _) = NewTool();
        var plan = await tool.PlanGrantPrivilegeToRolesAsync(
            roleIdsJson: "not json",
            tableLogicalName: "account",
            operation: "Read",
            depth: "BU",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_bulk_grant_invalid_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("not a valid JSON"));
    }
}
