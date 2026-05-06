using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyAppRoleAssignmentAgainstPlanToolTests
{
    private static (VerifyAppRoleAssignmentAgainstPlanTool Tool, Mock<IDataverseClient> Client, ProjectState State) BuildTool()
    {
        var state = new ProjectState { ProjectId = "test-session" };
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        var client = new Mock<IDataverseClient>();
        return (new VerifyAppRoleAssignmentAgainstPlanTool(client.Object, store.Object, ctx.Object), client, state);
    }

    private static string AssignPayload(Guid appId, Guid roleId) =>
        JsonSerializer.Serialize(new { action = "assign", appId, roleId, timestamp = DateTime.UtcNow });

    private static string UnassignPayload(Guid appId, Guid roleId) =>
        JsonSerializer.Serialize(new { action = "unassign", appId, roleId, timestamp = DateTime.UtcNow });

    [Fact]
    public async Task AssignVerify_PassWhenRolePresent()
    {
        var (tool, client, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var planId = $"assign_app_role_{Guid.NewGuid():N}";
        state.ExecutedPlans[planId] = AssignPayload(appId, roleId);

        client.Setup(c => c.ListAppRolesAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AppModuleRoleInfo(roleId, "System Customizer", null, true) });

        var result = await tool.VerifyAppRoleAssignmentAsync(planId, CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
        Assert.Empty(result.Discrepancies);
        Assert.Equal(appId, result.AppId);
        Assert.Equal(roleId, result.RoleId);
    }

    [Fact]
    public async Task AssignVerify_FailWhenRoleAbsent()
    {
        var (tool, client, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var planId = $"assign_app_role_{Guid.NewGuid():N}";
        state.ExecutedPlans[planId] = AssignPayload(appId, roleId);

        // List returns a different role — the assignment didn't actually take.
        client.Setup(c => c.ListAppRolesAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AppModuleRoleInfo(Guid.NewGuid(), "System Administrator", null, true) });

        var result = await tool.VerifyAppRoleAssignmentAsync(planId, CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Single(result.Discrepancies);
        Assert.Contains("expected to be assigned", result.Discrepancies[0]);
    }

    [Fact]
    public async Task UnassignVerify_PassWhenRoleAbsent()
    {
        var (tool, client, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var planId = $"unassign_app_role_{Guid.NewGuid():N}";
        state.ExecutedPlans[planId] = UnassignPayload(appId, roleId);

        client.Setup(c => c.ListAppRolesAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppModuleRoleInfo>());

        var result = await tool.VerifyAppRoleAssignmentAsync(planId, CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task UnassignVerify_FailWhenRoleStillPresent()
    {
        var (tool, client, state) = BuildTool();
        var appId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var planId = $"unassign_app_role_{Guid.NewGuid():N}";
        state.ExecutedPlans[planId] = UnassignPayload(appId, roleId);

        client.Setup(c => c.ListAppRolesAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new AppModuleRoleInfo(roleId, "System Customizer", null, true) });

        var result = await tool.VerifyAppRoleAssignmentAsync(planId, CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains("expected to be REMOVED", result.Discrepancies[0]);
    }

    [Fact]
    public async Task Verify_RejectsWrongPlanType()
    {
        var (tool, _, state) = BuildTool();
        // create_app_* prefix — wrong family for this verifier.
        var planId = $"create_app_{Guid.NewGuid():N}";
        state.ExecutedPlans[planId] = "irrelevant";

        var result = await tool.VerifyAppRoleAssignmentAsync(planId, CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains("WRONG PLAN TYPE", result.Discrepancies[0]);
    }

    [Fact]
    public async Task Verify_FailsWhenPlanNotInExecutedPlans()
    {
        var (tool, _, _) = BuildTool();
        var planId = $"assign_app_role_{Guid.NewGuid():N}";

        var result = await tool.VerifyAppRoleAssignmentAsync(planId, CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains("not found in ExecutedPlans", result.Discrepancies[0]);
    }
}
