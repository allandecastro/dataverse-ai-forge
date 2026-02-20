using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateTableToolTests
{
    private readonly PlanCreateTableTool _tool = new();

    [Fact]
    public async Task PlanCreateTable_ValidInput_ReturnsValidPlan()
    {
        var plan = await _tool.PlanCreateTableAsync(
            displayName: "Work Order",
            pluralDisplayName: "Work Orders",
            description: "Tracks maintenance work orders",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Work Order Title",
            primaryFieldMaxLength: 200,
            publisherPrefix: "cr123",
            solutionUniqueName: "MyTestSolution",
            hasNotes: true,
            hasActivities: false,
            changeTrackingEnabled: true,
            isAuditEnabled: true,
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("Work Order", plan.DisplayName);
        Assert.Equal("Work Orders", plan.PluralDisplayName);
        Assert.Equal("MyTestSolution", plan.SolutionUniqueName);
        Assert.NotEmpty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateTable_DerivedNames_AreCorrect()
    {
        var plan = await _tool.PlanCreateTableAsync(
            displayName: "Work Order",
            pluralDisplayName: "Work Orders",
            description: "Test",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Title",
            primaryFieldMaxLength: 100,
            publisherPrefix: "cr123",
            solutionUniqueName: "TestSol",
            hasNotes: false,
            hasActivities: false,
            changeTrackingEnabled: false,
            isAuditEnabled: false,
            CancellationToken.None);

        Assert.Equal("cr123_workorder", plan.TableLogicalName);
        Assert.Equal("cr123_WorkOrder", plan.TableSchemaName);
        Assert.Equal("cr123_workordername", plan.PrimaryFieldLogicalName);
    }

    [Fact]
    public async Task PlanCreateTable_PayloadContainsRequiredODataProperties()
    {
        var plan = await _tool.PlanCreateTableAsync(
            displayName: "Project",
            pluralDisplayName: "Projects",
            description: "Project tracking",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Project Name",
            primaryFieldMaxLength: 150,
            publisherPrefix: "cr123",
            solutionUniqueName: "TestSol",
            hasNotes: false,
            hasActivities: false,
            changeTrackingEnabled: false,
            isAuditEnabled: false,
            CancellationToken.None);

        Assert.Contains("Microsoft.Dynamics.CRM.EntityMetadata", plan.WebApiPayloadJson);
        Assert.Contains("Microsoft.Dynamics.CRM.StringAttributeMetadata", plan.WebApiPayloadJson);
        Assert.Contains("LocalizedLabels", plan.WebApiPayloadJson);
        Assert.Contains("cr123_Project", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateTable_RiskLevel_IsOrange()
    {
        var plan = await _tool.PlanCreateTableAsync(
            displayName: "Task",
            pluralDisplayName: "Tasks",
            description: "Task tracking",
            ownershipType: "UserOwned",
            primaryFieldDisplayName: "Task Name",
            primaryFieldMaxLength: 100,
            publisherPrefix: "cr123",
            solutionUniqueName: "TestSol",
            hasNotes: false,
            hasActivities: false,
            changeTrackingEnabled: false,
            isAuditEnabled: false,
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
