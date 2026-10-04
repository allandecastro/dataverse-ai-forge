using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PlanModifyFieldToolTests
{
    private static (PlanModifyFieldTool tool, Dictionary<string, ProjectState> store) NewTool()
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

        return (new PlanModifyFieldTool(mockStore.Object, mockCtx.Object), stored);
    }

    [Fact]
    public async Task PlanModifyField_AutoNumberFormat_OnString_AddsToPayload()
    {
        // This is how a table gets an autonumber identifier: set AutoNumberFormat
        // on the existing (primary name) String field, not a new column.
        var (tool, store) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "adc_gift",
            fieldLogicalName: "adc_name",
            modificationsJson: "{\"autoNumberFormat\":\"GIFT-{SEQNUM:5}\"}",
            currentMaxLength: 100,
            fieldType: "String",
            CancellationToken.None);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("\"AutoNumberFormat\":\"GIFT-{SEQNUM:5}\"", payload);
        Assert.Contains(plan.Modifications, m => m.Property == "AutoNumberFormat");
        Assert.Contains(plan.Warnings, w => w.Contains("autonumber"));
    }

    [Fact]
    public async Task PlanModifyField_AutoNumberFormat_OnNonString_Rejected()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "adc_gift",
            fieldLogicalName: "adc_amount",
            modificationsJson: "{\"autoNumberFormat\":\"X-{SEQNUM:5}\"}",
            currentMaxLength: 0,
            fieldType: "Integer",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("autoNumberFormat") && w.Contains("cannot be set on a Integer"));
    }

    [Fact]
    public async Task PlanModifyField_DisplayName_ReturnsModification()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_description",
            modificationsJson: "{\"displayName\":\"Project Description\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Single(plan.Modifications);
        Assert.Equal("DisplayName", plan.Modifications[0].Property);
        Assert.Equal("Project Description", plan.Modifications[0].NewValue);
        Assert.Contains("DisplayName", store["test-session"].PendingPlans[plan.PlanId]);
    }

    [Fact]
    public async Task PlanModifyField_RequiredLevel_ApplicationRequired_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_status",
            modificationsJson: "{\"requiredLevel\":\"ApplicationRequired\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("ApplicationRequired"));
    }

    [Fact]
    public async Task PlanModifyField_DecreaseMaxLength_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"maxLength\":100}",
            currentMaxLength: 200,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Decreasing MaxLength"));
    }

    [Fact]
    public async Task PlanModifyField_MultipleModifications_ReturnsAll()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"displayName\":\"Project Name\",\"isSearchable\":true,\"isAuditEnabled\":true}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.Equal(3, plan.Modifications.Count);
    }

    [Fact]
    public async Task PlanModifyField_InvalidProperty_ReturnsWarning()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"fieldType\":\"integer\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("cannot be modified"));
    }

    [Fact]
    public async Task PlanModifyField_RiskLevel_IsYellow()
    {
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_name",
            modificationsJson: "{\"displayName\":\"New Name\"}",
            currentMaxLength: 0,
            fieldType: "String",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanModifyField_TypeMismatch_ReturnsWarning()
    {
        // maxLength only applies to String/Memo — passing it on an Integer field
        // should warn, not silently accept it.
        var (tool, _) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_count",
            modificationsJson: "{\"maxLength\":500}",
            currentMaxLength: 0,
            fieldType: "Integer",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("'maxLength' cannot be set on a Integer"));
    }

    [Fact]
    public async Task PlanModifyField_NumericMinMax_OnDecimal_AppliesPrecision()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_amount",
            modificationsJson: "{\"minValue\":0,\"maxValue\":1000,\"precision\":2}",
            currentMaxLength: 0,
            fieldType: "Decimal",
            CancellationToken.None);

        Assert.Equal(3, plan.Modifications.Count);
        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("\"MinValue\":0", payload);
        Assert.Contains("\"MaxValue\":1000", payload);
        Assert.Contains("\"Precision\":2", payload);
        Assert.Contains("DecimalAttributeMetadata", payload);
    }

    [Fact]
    public async Task PlanModifyField_DateTimeBehavior_BuildsComplexValue()
    {
        var (tool, store) = NewTool();

        var plan = await tool.PlanModifyFieldAsync(
            tableLogicalName: "cr123_project",
            fieldLogicalName: "cr123_date",
            modificationsJson: "{\"dateTimeBehavior\":\"DateOnly\"}",
            currentMaxLength: 0,
            fieldType: "DateTime",
            CancellationToken.None);

        var payload = store["test-session"].PendingPlans[plan.PlanId];
        Assert.Contains("\"DateTimeBehavior\":{\"Value\":\"DateOnly\"}", payload);
        Assert.Contains(plan.Warnings, w => w.Contains("irreversible"));
    }
}
