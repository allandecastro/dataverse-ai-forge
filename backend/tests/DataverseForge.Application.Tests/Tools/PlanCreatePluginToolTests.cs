using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreatePluginToolTests
{
    private readonly PlanCreatePluginTool _tool = new();

    [Fact]
    public async Task PlanCreatePlugin_ValidInput_ReturnsPlan()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_workorder",
            pluginName: "PreValidateWorkOrderCreate",
            message: "Create",
            stage: 10,
            executionMode: "Synchronous",
            businessLogic: "Validate that the work order has a customer assigned",
            filteringAttributes: "",
            description: "Validates work order creation",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("PreValidateWorkOrderCreate", plan.PluginName);
        Assert.Equal("cr123_workorder", plan.TableLogicalName);
        Assert.Equal("Create", plan.Message);
        Assert.Equal(10, plan.Stage);
        Assert.Equal("Synchronous", plan.ExecutionMode);
        Assert.Equal("Validates work order creation", plan.Description);
        Assert.NotEmpty(plan.CSharpCode);
        Assert.NotEmpty(plan.RegistrationSteps);
        Assert.NotEmpty(plan.NuGetReferences);
        Assert.NotEmpty(plan.UnitTestCode);
        Assert.NotNull(plan.Warnings);
        Assert.NotNull(plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreatePlugin_InvalidStage_Warning()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_workorder",
            pluginName: "PostWorkOrderStage30",
            message: "Create",
            stage: 30,
            executionMode: "Synchronous",
            businessLogic: "Some logic",
            filteringAttributes: "",
            description: "Plugin with non-standard stage",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("30") && w.Contains("non-standard"));
    }

    [Fact]
    public async Task PlanCreatePlugin_InvalidMessage_Warning()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_workorder",
            pluginName: "CustomMessagePlugin",
            message: "CustomMessage",
            stage: 40,
            executionMode: "Synchronous",
            businessLogic: "Handle custom message",
            filteringAttributes: "",
            description: "Plugin with custom message",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("CustomMessage") && w.Contains("not a common"));
    }

    [Fact]
    public async Task PlanCreatePlugin_AsyncInPreStage_Warning()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_workorder",
            pluginName: "PreValidateAsyncPlugin",
            message: "Create",
            stage: 10,
            executionMode: "Asynchronous",
            businessLogic: "Async in pre-validation",
            filteringAttributes: "",
            description: "Async plugin in pre-validation stage",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("Asynchronous") && w.Contains("PreValidation"));
    }

    [Fact]
    public async Task PlanCreatePlugin_UpdateWithoutFilterAttributes_Warning()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_workorder",
            pluginName: "PostUpdateWorkOrder",
            message: "Update",
            stage: 40,
            executionMode: "Synchronous",
            businessLogic: "Handle update",
            filteringAttributes: "",
            description: "Update plugin without filtering attributes",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("filtering attributes"));
    }

    [Fact]
    public async Task PlanCreatePlugin_CSharpCodeContainsPlugin()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_invoice",
            pluginName: "PreValidateInvoiceCreate",
            message: "Create",
            stage: 10,
            executionMode: "Synchronous",
            businessLogic: "Validate invoice total",
            filteringAttributes: "",
            description: "Validates invoice on create",
            CancellationToken.None);

        Assert.Contains("IPlugin", plan.CSharpCode);
        Assert.Contains("Execute", plan.CSharpCode);
        Assert.Contains("ITracingService", plan.CSharpCode);
        Assert.Contains("try", plan.CSharpCode);
        Assert.Contains("InvalidPluginExecutionException", plan.CSharpCode);
        Assert.Contains("cr123_invoice", plan.CSharpCode);
        Assert.Contains("PreValidateInvoiceCreate", plan.CSharpCode);
    }

    [Fact]
    public async Task PlanCreatePlugin_UnitTestCodeContainsFakeXrmEasy()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_workorder",
            pluginName: "PostCreateWorkOrder",
            message: "Create",
            stage: 40,
            executionMode: "Synchronous",
            businessLogic: "Send notification on create",
            filteringAttributes: "",
            description: "Post-create notification plugin",
            CancellationToken.None);

        Assert.Contains("FakeXrmEasy", plan.UnitTestCode);
        Assert.Contains("Xunit", plan.UnitTestCode);
        Assert.Contains("PostCreateWorkOrder", plan.UnitTestCode);
    }

    [Fact]
    public async Task PlanCreatePlugin_RiskLevelIsOrange()
    {
        var plan = await _tool.PlanCreatePluginAsync(
            tableLogicalName: "cr123_workorder",
            pluginName: "PostCreateWorkOrder",
            message: "Create",
            stage: 40,
            executionMode: "Synchronous",
            businessLogic: "Any logic",
            filteringAttributes: "",
            description: "Any plugin",
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
