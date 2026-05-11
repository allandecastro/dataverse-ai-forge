using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanUpdateSecurityRoleToolTests
{
    private static readonly Guid RoleId = Guid.Parse("aaaa1111-aaaa-1111-aaaa-111111111111");
    private static readonly Guid BuId = Guid.Parse("aaaa2222-aaaa-2222-aaaa-222222222222");
    private static readonly Guid PrivAId = Guid.Parse("aaaa3333-aaaa-3333-aaaa-333333333333");
    private static readonly Guid PrivBId = Guid.Parse("aaaa4444-aaaa-4444-aaaa-444444444444");
    private static readonly Guid PrivCId = Guid.Parse("aaaa5555-aaaa-5555-aaaa-555555555555");

    private static (PlanUpdateSecurityRoleTool tool, Dictionary<string, ProjectState> store, Mock<IDataverseClient> mockClient) NewTool(
        bool isManaged = false,
        bool roleExists = true)
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
        mockCtx.SetupGet(c => c.SessionId).Returns("update-session");

        var mockClient = new Mock<IDataverseClient>();
        mockClient.Setup(c => c.GetSecurityRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(roleExists
                ? new SecurityRoleDetails(
                    Info: new SecurityRoleInfo(RoleId, "Feedback Reviewer", null, BuId, "Root", isManaged, false, null, null, null),
                    ParentRoleId: null,
                    IsCustomizable: !isManaged)
                : null);

        // Live privilege state: {A=Local, B=Local}
        mockClient.Setup(c => c.GetRolePrivilegesAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivAId, "Local", "prvA"),
                new(PrivBId, "Local", "prvB"),
            });

        // Table's privileges (adc_feedback): A=Read, B=Write, C=Append — all at all depths.
        mockClient.Setup(c => c.GetPrivilegesForTableAsync("adc_feedback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PrivilegeMetadata>
            {
                new(PrivAId, "prvReadadc_feedback", "Read", true, true, true, true),
                new(PrivBId, "prvWriteadc_feedback", "Write", true, true, true, true),
                new(PrivCId, "prvAppendadc_feedback", "Append", true, true, true, true),
            });

        return (new PlanUpdateSecurityRoleTool(mockStore.Object, mockCtx.Object, mockClient.Object), stored, mockClient);
    }

    [Fact]
    public async Task PlanUpdate_AddOnePrivilege_RemoveOnePrivilege_ComputesDelta()
    {
        var (tool, _, _) = NewTool();
        // Live = {Read, Write}. Target = {Read, Append}. Expect: add Append, remove Write.
        var target = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"BU\"},"
                   + "{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Append\",\"depth\":\"BU\"}]";

        var plan = await tool.PlanUpdateSecurityRoleAsync(
            roleId: RoleId,
            newName: "",
            newDescription: "",
            targetPrivilegesJson: target,
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("update_security_role_", plan.PlanId);
        Assert.Single(plan.PrivilegesToAdd);
        Assert.Equal(PrivCId, plan.PrivilegesToAdd[0].PrivilegeId);
        Assert.Single(plan.PrivilegesToRemove);
        Assert.Equal(PrivBId, plan.PrivilegesToRemove[0].PrivilegeId);
        Assert.Equal("orange", plan.RiskLevel); // remove present → orange
    }

    [Fact]
    public async Task PlanUpdate_DepthChangeOnSamePrivilege_ProducesAddOnly()
    {
        var (tool, _, _) = NewTool();
        // Live PrivA=Local. Target PrivA=Global (same id, different depth).
        // Expected: 1 add (depth swap upserts), 0 removes for PrivA. PrivB
        // also drops out of target so it lands in remove.
        var target = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"Org\"}]";

        var plan = await tool.PlanUpdateSecurityRoleAsync(
            roleId: RoleId,
            newName: "",
            newDescription: "",
            targetPrivilegesJson: target,
            cancellationToken: CancellationToken.None);

        Assert.Single(plan.PrivilegesToAdd);
        Assert.Equal(PrivAId, plan.PrivilegesToAdd[0].PrivilegeId);
        Assert.Equal("Global", plan.PrivilegesToAdd[0].Depth);
        Assert.Single(plan.PrivilegesToRemove);
        Assert.Equal(PrivBId, plan.PrivilegesToRemove[0].PrivilegeId);
    }

    [Fact]
    public async Task PlanUpdate_OnlyRename_NoPrivilegeChange_RiskGreen()
    {
        var (tool, _, _) = NewTool();
        // Target = current live set → no delta.
        var target = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"BU\"},"
                   + "{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Write\",\"depth\":\"BU\"}]";

        var plan = await tool.PlanUpdateSecurityRoleAsync(
            roleId: RoleId,
            newName: "Renamed Reviewer",
            newDescription: "",
            targetPrivilegesJson: target,
            cancellationToken: CancellationToken.None);

        Assert.Empty(plan.PrivilegesToAdd);
        Assert.Empty(plan.PrivilegesToRemove);
        Assert.Equal("Renamed Reviewer", plan.NewName);
        // Pure rename → green (no privilege change).
        Assert.Equal("green", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanUpdate_ManagedRole_Rejected()
    {
        var (tool, _, _) = NewTool(isManaged: true);

        var plan = await tool.PlanUpdateSecurityRoleAsync(
            roleId: RoleId,
            newName: "",
            newDescription: "",
            targetPrivilegesJson: "[]",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_update_role_managed_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("managed"));
    }

    [Fact]
    public async Task PlanUpdate_RoleNotFound_Rejected()
    {
        var (tool, _, _) = NewTool(roleExists: false);

        var plan = await tool.PlanUpdateSecurityRoleAsync(
            roleId: RoleId,
            newName: "",
            newDescription: "",
            targetPrivilegesJson: "[]",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_update_role_invalid_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("not found"));
    }

    [Fact]
    public async Task PlanUpdate_AddOnly_RiskYellow()
    {
        var (tool, _, _) = NewTool();
        // Target = live + Append (no removes).
        var target = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"BU\"},"
                   + "{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Write\",\"depth\":\"BU\"},"
                   + "{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Append\",\"depth\":\"BU\"}]";

        var plan = await tool.PlanUpdateSecurityRoleAsync(
            roleId: RoleId,
            newName: "",
            newDescription: "",
            targetPrivilegesJson: target,
            cancellationToken: CancellationToken.None);

        Assert.Single(plan.PrivilegesToAdd);
        Assert.Empty(plan.PrivilegesToRemove);
        Assert.Equal("yellow", plan.RiskLevel);
    }
}
