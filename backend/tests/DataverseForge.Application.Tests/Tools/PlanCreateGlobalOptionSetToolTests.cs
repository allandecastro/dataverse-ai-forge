using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateGlobalOptionSetToolTests
{
    private static (PlanCreateGlobalOptionSetTool tool, Dictionary<string, ProjectState> store, Mock<IDataverseClient> mockClient) NewTool()
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

        // By default the option set does NOT already exist — tests that exercise
        // the rejection path opt in by overriding this on the returned mock.
        var mockClient = new Mock<IDataverseClient>();
        mockClient.Setup(c => c.ResolveGlobalOptionSetMetadataIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        return (new PlanCreateGlobalOptionSetTool(mockStore.Object, mockCtx.Object, mockClient.Object), stored, mockClient);
    }

    private readonly PlanCreateGlobalOptionSetTool _tool;
    private readonly Dictionary<string, ProjectState> _store;
    private readonly Mock<IDataverseClient> _mockClient;

    public PlanCreateGlobalOptionSetToolTests()
    {
        (_tool, _store, _mockClient) = NewTool();
    }

    [Fact]
    public async Task PlanGlobalOptionSet_ValidInput_ReturnsValidPlan()
    {
        var optionsJson = "[{\"value\":100000,\"label\":\"Low\"},{\"value\":100001,\"label\":\"Medium\"},{\"value\":100002,\"label\":\"High\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_priority",
            displayName: "Priority",
            description: "Priority levels",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_priority", plan.Name);
        Assert.Equal("Priority", plan.DisplayName);
        Assert.Equal(3, plan.Options.Count);
        Assert.Contains("Microsoft.Dynamics.CRM.OptionSetMetadata", plan.WebApiPayloadJson);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanGlobalOptionSet_NoOptions_ReturnsWarning()
    {
        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_empty",
            displayName: "Empty",
            description: "No options",
            optionSetType: "Picklist",
            optionsJson: "[]",
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("no options"));
    }

    [Fact]
    public async Task PlanGlobalOptionSet_MissingPrefix_ReturnsWarning()
    {
        var optionsJson = "[{\"value\":100000,\"label\":\"Yes\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "priority",
            displayName: "Priority",
            description: "Priority levels",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("prefix"));
    }

    [Fact]
    public async Task PlanGlobalOptionSet_LowercaseJsonKeys_PopulateOptionValuesAndLabels()
    {
        // Regression: System.Text.Json is case-sensitive by default. The agent
        // serializes options with lowercase keys ("value", "label", "color")
        // matching the [Description] example. Without PropertyNameCaseInsensitive,
        // every option deserialized to Value=0, Label="", and the resulting
        // OData payload had three empty-labeled options. The downstream Choice
        // field creation then either failed at Dataverse or tripped Azure
        // OpenAI's content filter on the agent's recovery attempt.
        var optionsJson = "[{\"value\":100000000,\"label\":\"Physical\",\"color\":\"#2CA02C\"},"
                        + "{\"value\":100000001,\"label\":\"Digital\",\"color\":\"#1F77B4\"},"
                        + "{\"value\":100000002,\"label\":\"Service\",\"color\":\"#FF7F0E\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "toto_gifttype",
            displayName: "Gift Type",
            description: "Type of gift",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "toto",
            CancellationToken.None);

        Assert.Equal(3, plan.Options.Count);

        // Values must be the agent-provided numbers, not 0.
        Assert.Equal(100000000, plan.Options[0].Value);
        Assert.Equal(100000001, plan.Options[1].Value);
        Assert.Equal(100000002, plan.Options[2].Value);

        // Labels must round-trip — the bug surfaced as empty strings.
        Assert.Equal("Physical", plan.Options[0].Label);
        Assert.Equal("Digital", plan.Options[1].Label);
        Assert.Equal("Service", plan.Options[2].Label);

        // Colors must round-trip too (used to render colored badges in views).
        Assert.Equal("#2CA02C", plan.Options[0].Color);
        Assert.Equal("#1F77B4", plan.Options[1].Color);
        Assert.Equal("#FF7F0E", plan.Options[2].Color);

        // The serialized OData payload should embed the real labels,
        // not three empty-string defaults.
        Assert.Contains("Physical", plan.WebApiPayloadJson);
        Assert.Contains("Digital", plan.WebApiPayloadJson);
        Assert.Contains("Service", plan.WebApiPayloadJson);
        Assert.Contains("100000000", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanGlobalOptionSet_PascalCaseJsonKeys_AlsoWorks()
    {
        // Defensive: a future agent or external caller might serialize with
        // PascalCase. Case-insensitive deserialization should handle either.
        var optionsJson = "[{\"Value\":100000,\"Label\":\"Yes\",\"Color\":\"#2CA02C\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_yesno",
            displayName: "Yes/No",
            description: "Simple choice",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.Single(plan.Options);
        Assert.Equal(100000, plan.Options[0].Value);
        Assert.Equal("Yes", plan.Options[0].Label);
        Assert.Equal("#2CA02C", plan.Options[0].Color);
    }

    [Fact]
    public async Task PlanGlobalOptionSet_PayloadIncludesODataTypePerOption()
    {
        // Regression: each option in the OptionSetMetadata.Options array MUST
        // carry @odata.type=Microsoft.Dynamics.CRM.OptionMetadata. Without it
        // Dataverse returns HTTP 400 "unreadable payload" because the
        // polymorphic OptionMetadata collection can't be deserialized.
        var optionsJson = "[{\"value\":100000,\"label\":\"Delivered\",\"color\":\"#2CA02C\"},"
                        + "{\"value\":100001,\"label\":\"Canceled\",\"color\":\"#D62728\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_status",
            displayName: "Status",
            description: "Status",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.Contains("Microsoft.Dynamics.CRM.OptionMetadata", plan.WebApiPayloadJson);
        // The payload should have one OptionSetMetadata + one OptionMetadata per option.
        var optionMetadataCount = System.Text.RegularExpressions.Regex.Matches(
            plan.WebApiPayloadJson, "Microsoft.Dynamics.CRM.OptionMetadata").Count;
        Assert.Equal(2, optionMetadataCount);
    }

    [Fact]
    public async Task PlanGlobalOptionSet_MixedCaseName_LowercasedInPayloadAndPlan()
    {
        // Regression: agent passed "eppc_Type" (PascalCase) — Dataverse logical
        // names must be lowercase. Past failure: 400 with no useful body, agent
        // looped on retry. The tool now lowercases automatically + warns.
        var optionsJson = "[{\"value\":100000,\"label\":\"Draft\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "eppc_Type",
            displayName: "Type",
            description: "Type",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "eppc",
            CancellationToken.None);

        Assert.Equal("eppc_type", plan.Name);
        Assert.Contains("\"Name\": \"eppc_type\"", plan.WebApiPayloadJson);
        Assert.DoesNotContain("\"Name\": \"eppc_Type\"", plan.WebApiPayloadJson);
        Assert.Contains(plan.Warnings, w => w.Contains("lowercased"));
    }

    [Fact]
    public async Task PlanGlobalOptionSet_PersistsPayloadInPendingPlansByPlanId()
    {
        // Regression: agent was passing the literal string "{...payload for global option set...}"
        // as the OData payload to ExecuteCreateGlobalOptionSet, leading to a Dataverse 400
        // "Stream was not readable". Switched to planId-by-reference so the agent never
        // touches the JSON; the planner stores it and the executor looks it up.
        var optionsJson = "[{\"value\":100000,\"label\":\"Yes\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_yesno",
            displayName: "Yes/No",
            description: "Simple choice",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.StartsWith("create_global_option_set_", plan.PlanId);
        Assert.True(_store.ContainsKey("test-session"), "session state should exist after Plan");
        Assert.True(_store["test-session"].PendingPlans.ContainsKey(plan.PlanId),
            "payload should be stored under the returned planId");
        Assert.Equal(plan.WebApiPayloadJson, _store["test-session"].PendingPlans[plan.PlanId]);
    }

    [Fact]
    public async Task PlanGlobalOptionSet_RejectsWhenAlreadyExists()
    {
        // Regression: when the option set already exists in Dataverse, executing
        // the create plan throws "already exists", the plan stays orphaned in
        // PendingPlans, and the agent loops trying to "discard" it. Reject the
        // plan up-front so it never enters PendingPlans in the first place.
        _mockClient.Setup(c => c.ResolveGlobalOptionSetMetadataIdAsync(
                It.Is<string>(s => s == "eppc_gifttype"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var optionsJson = "[{\"value\":100000,\"label\":\"Physical\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "eppc_gifttype",
            displayName: "Gift Type",
            description: "Type of gift",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "eppc",
            CancellationToken.None);

        Assert.StartsWith("rejected_optionset_exists_", plan.PlanId);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("already exists"));
        // Rejected plan must NOT be persisted in PendingPlans.
        Assert.False(_store.TryGetValue("test-session", out var s) && s.PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanGlobalOptionSet_RiskLevel_IsYellow()
    {
        var optionsJson = "[{\"value\":100000,\"label\":\"Yes\"}]";

        var plan = await _tool.PlanCreateGlobalOptionSetAsync(
            name: "cr123_yesno",
            displayName: "Yes/No",
            description: "Simple choice",
            optionSetType: "Picklist",
            optionsJson: optionsJson,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
