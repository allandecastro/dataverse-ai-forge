using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateBpfToolTests
{
    private readonly PlanCreateBpfTool _tool = new();

    [Fact]
    public async Task PlanCreateBpf_ValidInput_ReturnsPlan()
    {
        var plan = await _tool.PlanCreateBpfAsync(
            bpfName: "cr123_WorkOrderProcess",
            displayName: "Work Order Process",
            description: "Guides work order lifecycle",
            primaryTable: "cr123_workorder",
            stagesDefinition: "qualify|Qualify|cr123_name:req,cr123_priority:opt|review|Review|cr123_status:req",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_WorkOrderProcess", plan.BpfName);
        Assert.Equal("Work Order Process", plan.DisplayName);
        Assert.Equal("Guides work order lifecycle", plan.Description);
        Assert.Equal("cr123_workorder", plan.PrimaryTable);
        Assert.Equal(2, plan.Stages.Count);
        Assert.NotEmpty(plan.StageFlowDiagram);
        Assert.NotEmpty(plan.WebApiPayloadJson);
        Assert.NotNull(plan.Warnings);
        Assert.NotNull(plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateBpf_EmptyStages_Warning()
    {
        var plan = await _tool.PlanCreateBpfAsync(
            bpfName: "cr123_EmptyBpf",
            displayName: "Empty BPF",
            description: "No stages",
            primaryTable: "cr123_workorder",
            stagesDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("No stages"));
    }

    [Fact]
    public async Task PlanCreateBpf_StageWithTooManySteps_Warning()
    {
        // Build a single stage with 6 steps (exceeds the 5-step recommendation)
        var stagesDefinition = "overloaded|Overloaded Stage|f1:req,f2:req,f3:opt,f4:opt,f5:req,f6:opt";

        var plan = await _tool.PlanCreateBpfAsync(
            bpfName: "cr123_TooManySteps",
            displayName: "Too Many Steps BPF",
            description: "Stage with too many steps",
            primaryTable: "cr123_workorder",
            stagesDefinition: stagesDefinition,
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("6 steps"));
    }

    [Fact]
    public async Task PlanCreateBpf_StageWithNoSteps_Warning()
    {
        // The pipe split uses RemoveEmptyEntries|TrimEntries, so the third part must be
        // non-empty after trimming. ",," survives the pipe split but yields 0 entries
        // when split by comma with RemoveEmptyEntries, producing a stage with no steps.
        var stagesDefinition = "empty|Empty Stage|,,";

        var plan = await _tool.PlanCreateBpfAsync(
            bpfName: "cr123_NoSteps",
            displayName: "No Steps BPF",
            description: "Stage without steps",
            primaryTable: "cr123_workorder",
            stagesDefinition: stagesDefinition,
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("no steps"));
    }

    [Fact]
    public async Task PlanCreateBpf_StageFlowDiagram_ContainsStageNames()
    {
        var plan = await _tool.PlanCreateBpfAsync(
            bpfName: "cr123_FlowTest",
            displayName: "Flow Test",
            description: "Diagram test",
            primaryTable: "cr123_workorder",
            stagesDefinition: "qualify|Qualify|cr123_name:req,cr123_priority:opt|review|Review|cr123_status:req",
            CancellationToken.None);

        Assert.Contains("Qualify", plan.StageFlowDiagram);
        Assert.Contains("Review", plan.StageFlowDiagram);
        Assert.Contains("[", plan.StageFlowDiagram);
        Assert.Contains("]", plan.StageFlowDiagram);
    }

    [Fact]
    public async Task PlanCreateBpf_PayloadContainsCategory4()
    {
        var plan = await _tool.PlanCreateBpfAsync(
            bpfName: "cr123_CategoryTest",
            displayName: "Category Test",
            description: "Payload test",
            primaryTable: "cr123_workorder",
            stagesDefinition: "stage1|Stage One|cr123_name:req",
            CancellationToken.None);

        Assert.Contains("\"category\": 4", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateBpf_RiskLevelIsOrange()
    {
        var plan = await _tool.PlanCreateBpfAsync(
            bpfName: "cr123_RiskTest",
            displayName: "Risk Test",
            description: "Risk level test",
            primaryTable: "cr123_workorder",
            stagesDefinition: "stage1|Stage One|cr123_name:req",
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
