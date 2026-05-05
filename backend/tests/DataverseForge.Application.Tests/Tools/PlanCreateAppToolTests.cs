using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateAppToolTests
{
    // Stable GUID returned by the default-icon web-resource lookup in tests.
    private static readonly Guid DefaultIconId = Guid.Parse("953b9fac-1e5e-e611-80d6-00155ded156f");

    private static (PlanCreateAppTool Tool, Mock<IDataverseClient> Client, Mock<IProjectStateStore> Store, Dictionary<string, ProjectState> Stored) BuildTool(
        string? publisherPrefix = null,
        string? solutionUniqueName = null,
        Guid? iconResolverReturns = null,
        bool iconResolverReturnsNull = false)
    {
        var stored = new Dictionary<string, ProjectState>();
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) =>
            {
                if (!stored.TryGetValue(id, out var s))
                {
                    s = new ProjectState
                    {
                        ProjectId = id,
                        PublisherPrefix = publisherPrefix ?? string.Empty,
                        SolutionUniqueName = solutionUniqueName ?? string.Empty,
                    };
                    stored[id] = s;
                }
                return s;
            });
        store.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Callback((string id, ProjectState s, CancellationToken _) => stored[id] = s)
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");

        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.ResolveWebResourceIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(iconResolverReturnsNull ? (Guid?)null : (iconResolverReturns ?? DefaultIconId));
        return (new PlanCreateAppTool(client.Object, store.Object, ctx.Object), client, store, stored);
    }

    [Fact]
    public async Task PlanCreateApp_HappyPath_StoresPayloadAndReturnsOrangePlanId()
    {
        var (tool, _, _, stored) = BuildTool(publisherPrefix: "eppc");

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo Hub",
            uniqueName: "eppc_DemoHub",
            description: "A demo app",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("create_app_", plan.PlanId);
        Assert.Equal("orange", plan.RiskLevel);
        Assert.Equal("eppc_demohub", plan.UniqueName); // lowercased
        Assert.Equal(4, plan.ClientType);
        Assert.Equal(0, plan.NavigationType);
        Assert.Contains("\"name\": \"Demo Hub\"", plan.WebApiPayloadJson);
        Assert.Contains("\"uniquename\": \"eppc_demohub\"", plan.WebApiPayloadJson);
        // OOB icon web resource resolved at plan time and embedded as a raw GUID —
        // appmodule.webresourceid is a UniqueIdentifierAttribute, NOT a LookupAttribute,
        // so @odata.bind would be rejected by Dataverse.
        Assert.Contains($"\"webresourceid\": \"{DefaultIconId}\"", plan.WebApiPayloadJson);

        // Payload stored server-side under planId
        Assert.True(stored["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanCreateApp_RejectsWhenOobIconCannotBeResolved()
    {
        var (tool, _, _, _) = BuildTool(iconResolverReturnsNull: true);

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo",
            uniqueName: "eppc_demo",
            description: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_create_app_", plan.PlanId);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("OOB default icon"));
    }

    [Fact]
    public async Task PlanCreateApp_UsesCustomWebResourceId_WhenSupplied()
    {
        var (tool, client, _, _) = BuildTool();
        var customId = Guid.NewGuid();

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo",
            uniqueName: "eppc_demo",
            description: "",
            cancellationToken: CancellationToken.None,
            webResourceId: customId.ToString());

        Assert.StartsWith("create_app_", plan.PlanId);
        Assert.Contains($"\"webresourceid\": \"{customId}\"", plan.WebApiPayloadJson);
        // No call to the resolver when the user supplied a custom GUID.
        client.Verify(c => c.ResolveWebResourceIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlanCreateApp_RejectsInvalidCustomWebResourceId()
    {
        var (tool, _, _, _) = BuildTool();

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo",
            uniqueName: "eppc_demo",
            description: "",
            cancellationToken: CancellationToken.None,
            webResourceId: "not-a-guid");

        Assert.StartsWith("rejected_create_app_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("not a valid GUID"));
    }

    [Fact]
    public async Task PlanCreateApp_RejectsEmptyName()
    {
        var (tool, _, _, _) = BuildTool();

        var plan = await tool.PlanCreateAppAsync(
            name: "",
            uniqueName: "eppc_X",
            description: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_create_app_", plan.PlanId);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("name is required"));
    }

    [Fact]
    public async Task PlanCreateApp_RejectsEmptyUniqueName()
    {
        var (tool, _, _, _) = BuildTool();

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo Hub",
            uniqueName: "",
            description: "",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("rejected_create_app_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("uniqueName is required"));
    }

    [Fact]
    public async Task PlanCreateApp_RejectsInvalidClientType()
    {
        var (tool, _, _, _) = BuildTool();

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo",
            uniqueName: "eppc_demo",
            description: "",
            cancellationToken: CancellationToken.None,
            clientType: 999);

        Assert.StartsWith("rejected_create_app_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("clientType 999"));
    }

    [Fact]
    public async Task PlanCreateApp_RejectsInvalidNavigationType()
    {
        var (tool, _, _, _) = BuildTool();

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo",
            uniqueName: "eppc_demo",
            description: "",
            cancellationToken: CancellationToken.None,
            navigationType: 9);

        Assert.StartsWith("rejected_create_app_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("navigationType 9"));
    }

    [Fact]
    public async Task PlanCreateApp_LowercasesMixedCaseUniqueName_AndWarns()
    {
        var (tool, _, _, _) = BuildTool(publisherPrefix: "eppc");

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo",
            uniqueName: "EPPC_DemoHub",
            description: "",
            cancellationToken: CancellationToken.None);

        Assert.Equal("eppc_demohub", plan.UniqueName);
        Assert.Contains(plan.Warnings, w => w.Contains("lowercased"));
    }

    [Fact]
    public async Task PlanCreateApp_WarnsWhenUniqueNameMissingPublisherPrefix()
    {
        var (tool, _, _, _) = BuildTool(publisherPrefix: "eppc");

        var plan = await tool.PlanCreateAppAsync(
            name: "Demo",
            uniqueName: "other_demo",
            description: "",
            cancellationToken: CancellationToken.None);

        // Plan still succeeds (warning, not rejection — some users want
        // a different publisher) but flags the mismatch.
        Assert.StartsWith("create_app_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("does not start with the active publisher prefix"));
    }
}
