using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateFieldToolTests
{
    private readonly PlanCreateFieldTool _tool = new();

    [Fact]
    public async Task PlanCreateField_StringType_BuildsCorrectPayload()
    {
        var plan = await _tool.PlanCreateFieldAsync(
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

        Assert.Contains("Microsoft.Dynamics.CRM.StringAttributeMetadata", plan.WebApiPayloadJson);
        Assert.Contains("500", plan.WebApiPayloadJson);
        Assert.Equal("cr123_description", plan.FieldLogicalName);
        Assert.Equal("string", plan.FieldType);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanCreateField_BooleanType_IncludesLabels()
    {
        var plan = await _tool.PlanCreateFieldAsync(
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

        Assert.Contains("Microsoft.Dynamics.CRM.BooleanAttributeMetadata", plan.WebApiPayloadJson);
        Assert.Contains("Urgent", plan.WebApiPayloadJson);
        Assert.Contains("Normal", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateField_LocalOptionSet_IncludesOptions()
    {
        var configJson = "{\"options\":[{\"label\":\"Low\",\"value\":100000000},{\"label\":\"Medium\",\"value\":100000001},{\"label\":\"High\",\"value\":100000002}]}";

        var plan = await _tool.PlanCreateFieldAsync(
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

        Assert.Contains("Microsoft.Dynamics.CRM.PicklistAttributeMetadata", plan.WebApiPayloadJson);
        Assert.Contains("Low", plan.WebApiPayloadJson);
        Assert.Contains("Medium", plan.WebApiPayloadJson);
        Assert.Contains("High", plan.WebApiPayloadJson);
        Assert.Contains("100000000", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateField_LookupType_ReturnsWarning()
    {
        var plan = await _tool.PlanCreateFieldAsync(
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
        var plan = await _tool.PlanCreateFieldAsync(
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
}
