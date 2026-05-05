using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanUpdateAppToolTests
{
    private static (PlanUpdateAppTool Tool, Mock<IDataverseClient> Client, Dictionary<string, ProjectState> Stored) BuildTool()
    {
        var stored = new Dictionary<string, ProjectState>();
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s)) { s = new ProjectState { ProjectId = id }; stored[id] = s; }
                return s;
            });
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        var client = new Mock<IDataverseClient>();
        return (new PlanUpdateAppTool(client.Object, store.Object, ctx.Object), client, stored);
    }

    [Fact]
    public async Task PlanUpdateApp_HappyPath_StoresPayloadAndReturnsYellowPlan()
    {
        var (tool, client, stored) = BuildTool();
        var appId = Guid.NewGuid();

        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppModuleDetails(appId, "Old", "eppc_demo", "old desc", "", 4, 0, 0, 1, false, null, null, null));

        var plan = await tool.PlanUpdateAppAsync(
            appId: appId.ToString(),
            name: "Demo Hub Updated",
            description: "New description",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("update_app_", plan.PlanId);
        Assert.Equal("yellow", plan.RiskLevel);
        Assert.Equal(appId, plan.AppId);
        Assert.Contains("\"name\": \"Demo Hub Updated\"", plan.WebApiPayloadJson);
        Assert.True(stored["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanUpdateApp_RejectsInvalidGuid()
    {
        var (tool, _, _) = BuildTool();

        var plan = await tool.PlanUpdateAppAsync(
            appId: "not-a-guid",
            name: "Demo",
            description: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_update_app_", plan.PlanId);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("appId must be a valid GUID"));
    }

    [Fact]
    public async Task PlanUpdateApp_NullExistenceCheck_WarnsButProceeds()
    {
        // Real session 2026-05-05: a freshly-created appmodule whose initial
        // publish failed was hidden from GetAppAsync for minutes, even though
        // it was visible in the maker portal. PlanUpdateApp must NOT hard-
        // reject on null — it should warn and proceed, letting the downstream
        // PATCH (with If-Match: *) reject if the id is truly bogus.
        var (tool, client, _) = BuildTool();
        var appId = Guid.NewGuid();
        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AppModuleDetails?)null);

        var plan = await tool.PlanUpdateAppAsync(
            appId: appId.ToString(),
            name: "Demo",
            description: "",
            cancellationToken: CancellationToken.None);

        // Plan succeeds (yellow risk) with a warning, NOT a hard reject.
        Assert.StartsWith("update_app_", plan.PlanId);
        Assert.Equal("yellow", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("Could not verify app", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanUpdateApp_WarnsOnManagedApp()
    {
        var (tool, client, _) = BuildTool();
        var appId = Guid.NewGuid();
        client.Setup(c => c.GetAppAsync(appId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AppModuleDetails(
                appId, "Sales Hub", "msdyn_saleshub", "", "",
                4, 0, 0, 1, IsManaged: true, null, null, null));

        var plan = await tool.PlanUpdateAppAsync(
            appId: appId.ToString(),
            name: "Renamed",
            description: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("update_app_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("MANAGED"));
    }
}
