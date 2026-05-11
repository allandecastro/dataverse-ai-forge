using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateSecurityRoleToolTests
{
    private static readonly Guid SampleBuId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PrivReadId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PrivWriteId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (PlanCreateSecurityRoleTool tool, Dictionary<string, ProjectState> store, Mock<IDataverseClient> mockClient) NewTool()
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
        mockCtx.SetupGet(c => c.SessionId).Returns("test-session");

        var mockClient = new Mock<IDataverseClient>();
        // Default: no existing role with the requested name.
        mockClient.Setup(c => c.GetSecurityRolesAsync(It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityRoleInfo>());
        // Default: privileges for adc_feedback table — Read + Write at all four depths.
        mockClient.Setup(c => c.GetPrivilegesForTableAsync("adc_feedback", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PrivilegeMetadata>
            {
                new(PrivReadId, "prvReadadc_feedback", "Read", true, true, true, true),
                new(PrivWriteId, "prvWriteadc_feedback", "Write", true, true, false, true),
            });

        return (new PlanCreateSecurityRoleTool(mockStore.Object, mockCtx.Object, mockClient.Object), stored, mockClient);
    }

    [Fact]
    public async Task PlanCreateRole_HappyPath_ReturnsPendingPlan()
    {
        var (tool, store, _) = NewTool();
        var privileges = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"BU\"},"
                       + "{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Write\",\"depth\":\"BU\"}]";

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Feedback Reviewer",
            description: "Read+Write feedback at BU depth",
            businessUnitId: SampleBuId,
            privilegesJson: privileges,
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("create_security_role_", plan.PlanId);
        Assert.Equal("Feedback Reviewer", plan.Name);
        Assert.Equal(SampleBuId, plan.BusinessUnitId);
        Assert.Equal(2, plan.Privileges.Count);
        Assert.All(plan.Privileges, p => Assert.Equal("Local", p.Depth));
        Assert.Empty(plan.Warnings);
        Assert.True(store["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanCreateRole_EmptyName_RejectsPlan()
    {
        var (tool, _, _) = NewTool();

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "",
            description: "",
            businessUnitId: SampleBuId,
            privilegesJson: "[]",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_role_invalid_", plan.PlanId);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("Role name is required"));
    }

    [Fact]
    public async Task PlanCreateRole_MissingBu_RejectsPlan()
    {
        var (tool, _, _) = NewTool();

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Sample Role",
            description: "",
            businessUnitId: Guid.Empty,
            privilegesJson: "[]",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_role_invalid_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("BusinessUnitId is required"));
    }

    [Fact]
    public async Task PlanCreateRole_DuplicateName_RejectsPlan()
    {
        var (tool, _, mockClient) = NewTool();
        mockClient.Setup(c => c.GetSecurityRolesAsync("Feedback Reviewer", SampleBuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecurityRoleInfo>
            {
                new(Guid.NewGuid(), "Feedback Reviewer", null, SampleBuId, "Root", false, false, null, null, null),
            });

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Feedback Reviewer",
            description: "",
            businessUnitId: SampleBuId,
            privilegesJson: "[]",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_role_exists_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("already exists"));
    }

    [Fact]
    public async Task PlanCreateRole_InvalidDepth_RejectsPlan()
    {
        var (tool, _, _) = NewTool();
        // CanBeDeep=false on the Write privilege per NewTool() setup; ParentChild
        // (Deep) is therefore not allowed and the planner must refuse.
        var privileges = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Write\",\"depth\":\"ParentChild\"}]";

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Bad Depth Role",
            description: "",
            businessUnitId: SampleBuId,
            privilegesJson: privileges,
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_role_invalid_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("does not allow depth"));
    }

    [Fact]
    public async Task PlanCreateRole_UnknownOperation_RejectsPlan()
    {
        var (tool, _, _) = NewTool();
        var privileges = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Cuddle\",\"depth\":\"BU\"}]";

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Bogus Role",
            description: "",
            businessUnitId: SampleBuId,
            privilegesJson: privileges,
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_role_invalid_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("Unknown operation"));
    }

    [Fact]
    public async Task PlanCreateRole_EmptyMatrix_WarnsButProceeds()
    {
        var (tool, store, _) = NewTool();

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Empty Role",
            description: "Inherits via template",
            businessUnitId: SampleBuId,
            privilegesJson: "[]",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("create_security_role_", plan.PlanId);
        Assert.Empty(plan.Privileges);
        Assert.Contains(plan.Warnings, w => w.Contains("NO privileges"));
        Assert.True(store["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanCreateRole_WithRoleTemplate_BindsTemplateInPayload()
    {
        var (tool, store, _) = NewTool();
        var templateId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var privileges = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"BU\"}]";

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Multi-Org Reviewer",
            description: "Auto-replicates into every new BU",
            businessUnitId: SampleBuId,
            privilegesJson: privileges,
            cancellationToken: CancellationToken.None,
            roleTemplateId: templateId);

        Assert.StartsWith("create_security_role_", plan.PlanId);
        // Template binding is in the stored payload, not the plan record — peek
        // at the staged JSON to confirm it landed under roletemplateid@odata.bind.
        var staged = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains($"/roletemplates({templateId})", staged);
        // Warning surfaces the multi-BU replication behavior so the user knows.
        Assert.Contains(plan.Warnings, w => w.Contains("auto-replicate"));
    }

    [Fact]
    public async Task PlanCreateRole_NoTemplate_OmitsTemplateBinding()
    {
        var (tool, store, _) = NewTool();
        var privileges = "[{\"tableLogicalName\":\"adc_feedback\",\"operation\":\"Read\",\"depth\":\"BU\"}]";

        var plan = await tool.PlanCreateSecurityRoleAsync(
            name: "Single-BU Reviewer",
            description: "Standard custom role",
            businessUnitId: SampleBuId,
            privilegesJson: privileges,
            cancellationToken: CancellationToken.None);

        var staged = store["test-session"].PendingPlans[plan.PlanId];
        Assert.DoesNotContain("roletemplateid", staged);
        // No template → no auto-replicate warning either.
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("auto-replicate"));
    }
}
