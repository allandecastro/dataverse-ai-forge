using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateFieldToolTests
{
    private static (PlanCreateFieldTool tool, Dictionary<string, ProjectState> store) NewTool()
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

        // Default: empty table — no conflict warnings.
        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTableDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableDetails(
                "x", "X", "X", true, false, "UserOwned", "xid", "name",
                Array.Empty<ColumnInfo>(), Array.Empty<RelationshipInfo>(), Array.Empty<AlternateKeyInfo>()));

        return (new PlanCreateFieldTool(mockStore.Object, mockCtx.Object, mockClient.Object), stored);
    }

    [Fact]
    public async Task PlanCreateField_StringType_BuildsCorrectPayload()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Description",
            description: "Work order description",
            fieldType: "string",
            fieldTypeConfigJson: "{\"maxLength\":500}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: false,
            publisherPrefix: "cr123",
            CancellationToken.None);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("Microsoft.Dynamics.CRM.StringAttributeMetadata", payload);
        Assert.Contains("500", payload);
        Assert.Equal("cr123_description", plan.FieldLogicalName);
        Assert.Equal("string", plan.FieldType);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanCreateField_BooleanType_IncludesLabels()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Is Urgent",
            description: "Whether the order is urgent",
            fieldType: "boolean",
            fieldTypeConfigJson: "{\"trueLabel\":\"Urgent\",\"falseLabel\":\"Normal\"}",
            requiredLevel: "None",
            isSearchable: false,
            isAuditEnabled: false,
            publisherPrefix: "cr123",
            CancellationToken.None);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("Microsoft.Dynamics.CRM.BooleanAttributeMetadata", payload);
        Assert.Contains("Urgent", payload);
        Assert.Contains("Normal", payload);
    }

    [Fact]
    public async Task PlanCreateField_LocalOptionSet_RejectedWithoutOptIn()
    {
        var (tool, store) = NewTool();
        var configJson = "{\"options\":[{\"label\":\"Low\",\"value\":100000000},{\"label\":\"Medium\",\"value\":100000001},{\"label\":\"High\",\"value\":100000002}]}";

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Priority",
            description: "Priority level",
            fieldType: "localOptionSet",
            fieldTypeConfigJson: configJson,
            requiredLevel: "ApplicationRequired",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "cr123",
            CancellationToken.None);

        // Default behavior: local option set is rejected; agent must use globalOptionSet
        // or pass allowLocalOptionSet:true to override. Rejection short-circuits before
        // any state is saved, so the store dict for "test-session" never gets created.
        Assert.StartsWith("rejected_local_optionset_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("REJECTED", StringComparison.OrdinalIgnoreCase));
        Assert.False(store.ContainsKey("test-session"));
    }

    [Fact]
    public async Task PlanCreateField_LocalOptionSet_AllowedWithOptIn()
    {
        var (tool, store) = NewTool();
        var configJson = "{\"allowLocalOptionSet\":true,\"options\":[{\"label\":\"Low\",\"value\":100000000},{\"label\":\"Medium\",\"value\":100000001},{\"label\":\"High\",\"value\":100000002}]}";

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Priority",
            description: "Priority level",
            fieldType: "localOptionSet",
            fieldTypeConfigJson: configJson,
            requiredLevel: "ApplicationRequired",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "cr123",
            CancellationToken.None);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("Microsoft.Dynamics.CRM.PicklistAttributeMetadata", payload);
        Assert.Contains("Low", payload);
        Assert.Contains("Medium", payload);
        Assert.Contains("High", payload);
        Assert.Contains("100000000", payload);
    }

    [Fact]
    public async Task PlanCreateField_LookupType_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Customer",
            description: "Customer reference",
            fieldType: "lookup",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: false,
            isAuditEnabled: false,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("1:N relationship"));
    }

    [Fact]
    public async Task PlanCreateField_RiskLevel_IsYellow()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Notes",
            description: "Additional notes",
            fieldType: "memo",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: false,
            isAuditEnabled: false,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateField_ExactMatchExists_ReturnsRejectedPlanWithoutPersisting()
    {
        // Regression: when a previous batch partially succeeded, a retry batch was
        // re-creating already-existing fields and getting Dataverse 400s. The agent
        // saw "FAILED" and looped. plan_create_field should now refuse to plan a
        // duplicate so the agent can't accidentally include it in the retry.
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

        // Live Dataverse already has cr123_description on the table.
        var existing = new ColumnInfo(
            LogicalName: "cr123_description",
            DisplayName: "Description",
            AttributeType: "MemoType",
            IsCustom: true,
            RequiredLevel: "None",
            MaxLength: 2000,
            IsSearchable: true);
        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTableDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableDetails(
                "cr123_workorder", "WorkOrder", "WorkOrders", true, false, "UserOwned",
                "cr123_workorderid", "cr123_name",
                new[] { existing }, Array.Empty<RelationshipInfo>(), Array.Empty<AlternateKeyInfo>()));

        var tool = new PlanCreateFieldTool(mockStore.Object, mockCtx.Object, mockClient.Object);

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Description",
            description: "Work order description",
            fieldType: "memo",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: false,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.StartsWith("rejected_field_exists_", plan.PlanId);
        Assert.Equal("red", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("REJECTED"));
        Assert.Contains(plan.Warnings, w => w.Contains("DO NOT include"));
        // Rejection short-circuits — no payload stored, no batch can execute it.
        Assert.False(stored.TryGetValue("test-session", out var s) && s.PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanCreateField_SimilarButNotExactMatch_StillPlansWithWarning()
    {
        // A 'description2' field shouldn't block a plan for 'description' — only the
        // exact-match path is a hard stop. Similar matches are surfaced as warnings.
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

        var existing = new ColumnInfo(
            LogicalName: "cr123_descriptionold",
            DisplayName: "Description Old",
            AttributeType: "MemoType",
            IsCustom: true,
            RequiredLevel: "None",
            MaxLength: 2000,
            IsSearchable: true);
        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTableDetailsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableDetails(
                "cr123_workorder", "WorkOrder", "WorkOrders", true, false, "UserOwned",
                "cr123_workorderid", "cr123_name",
                new[] { existing }, Array.Empty<RelationshipInfo>(), Array.Empty<AlternateKeyInfo>()));

        var tool = new PlanCreateFieldTool(mockStore.Object, mockCtx.Object, mockClient.Object);

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_workorder",
            displayName: "Description",
            description: "Work order description",
            fieldType: "memo",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: false,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.StartsWith("create_field_", plan.PlanId);
        Assert.Equal("yellow", plan.RiskLevel);
        Assert.Contains(plan.Warnings, w => w.Contains("Similar"));
        Assert.True(stored["test-session"].PendingPlans.ContainsKey(plan.PlanId));
    }

    [Fact]
    public async Task PlanCreateField_DisplayName_WithSpecialChars_SanitizesSchemaName()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "adc_vehicle",
            displayName: "Vehicle Identification Number (VIN)",
            description: "VIN",
            fieldType: "string",
            fieldTypeConfigJson: "{\"maxLength\":17}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "adc",
            CancellationToken.None);

        // No parens / spaces leak into the schema or logical name
        Assert.DoesNotContain("(", plan.FieldSchemaName);
        Assert.DoesNotContain(")", plan.FieldSchemaName);
        Assert.DoesNotContain(" ", plan.FieldLogicalName);
        Assert.Equal("adc_vehicleidentificationnumbervin", plan.FieldLogicalName);
        Assert.NotEmpty(plan.Warnings);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.DoesNotContain("\"SchemaName\":\"adc_VehicleIdentificationNumber(VIN)\"", payload);
    }

    [Fact]
    public async Task PlanCreateField_Lookup_IsRejectedAndPointsToRelationshipTool()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_order",
            displayName: "Customer",
            description: "",
            fieldType: "lookup",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.StartsWith("rejected_lookup_field_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("plan_create_one_to_many_relationship"));
    }

    [Fact]
    public async Task PlanCreateField_Customer_IsRejectedWithCustomerLookupHint()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_order",
            displayName: "Buyer",
            description: "",
            fieldType: "customer",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.StartsWith("rejected_customer_field_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("plan_create_customer_lookup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlanCreateField_AutoNumber_IncludesMaxLength()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_order",
            displayName: "Order Number",
            description: "",
            fieldType: "autoNumber",
            fieldTypeConfigJson: "{\"format\":\"ORD-{SEQNUM:5}\"}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "cr123",
            CancellationToken.None);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        // Past failure: missing MaxLength returned "string attributes require a max length value" 400.
        Assert.Contains("\"MaxLength\":100", payload);
        Assert.Contains("\"AutoNumberFormat\":\"ORD-{SEQNUM:5}\"", payload);
        Assert.Contains("StringAttributeMetadata", payload);
    }

    [Fact]
    public async Task PlanCreateField_Calculated_IsRejectedWithMakerPortalHint()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_order",
            displayName: "Total Value",
            description: "",
            fieldType: "calculated",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.StartsWith("rejected_calculated_field_", plan.PlanId);
        Assert.Contains(plan.Warnings, w => w.Contains("maker portal"));
    }

    [Fact]
    public async Task PlanCreateField_Rollup_BuildsIntegerPlaceholder()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanCreateFieldAsync(
            tableLogicalName: "cr123_order",
            displayName: "Item Count",
            description: "",
            fieldType: "rollup",
            fieldTypeConfigJson: "{}",
            requiredLevel: "None",
            isSearchable: true,
            isAuditEnabled: true,
            publisherPrefix: "cr123",
            CancellationToken.None);

        Assert.StartsWith("create_field_", plan.PlanId);
        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("IntegerAttributeMetadata", payload);
        Assert.Contains("\"SourceType\":2", payload);
        Assert.Contains(plan.Warnings, w => w.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase));
    }
}
