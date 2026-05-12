using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Smoke tests for the four AB lifecycle tools — Activate, Deactivate,
/// Delete, plus the rename plan/execute pair. Happy path + the two
/// refusals that matter most (managed, activated-blocks-delete).
/// </summary>
public class WorkflowLifecycleToolsTests
{
    private static readonly Guid Wf = Guid.Parse("aaaa1111-bbbb-2222-cccc-3333dddd4444");
    private readonly Mock<IDataverseClient> _dataverse = new();
    private readonly Mock<IProjectStateStore> _store = new();
    private readonly Mock<ISessionContext> _ctx = new();

    private ProjectState SetupSession(string sessionId, WorkflowInfo? liveWorkflow)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _ctx.Setup(c => c.SessionId).Returns(sessionId);
        _store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _store.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _dataverse.Setup(c => c.GetWorkflowAsync(Wf, It.IsAny<CancellationToken>())).ReturnsAsync(liveWorkflow);
        return state;
    }

    private static WorkflowInfo MakeWorkflow(string name, bool activated, bool managed = false,
        WorkflowCategory category = WorkflowCategory.BusinessRule) =>
        new(Wf, name, "desc", category, "account", activated, managed, !managed, 1, DateTime.UtcNow);

    // ── Activate ────────────────────────────────────────────────────────

    [Fact]
    public async Task Activate_Inactive_FiresApiAndPushesUndo()
    {
        var state = SetupSession("act-1", MakeWorkflow("Rule X", activated: false));
        _dataverse.Setup(c => c.ActivateWorkflowAsync(Wf, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tool = new ActivateWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<ActivateWorkflowTool>.Instance);

        var result = await tool.ActivateWorkflowAsync(Wf, CancellationToken.None);

        Assert.Contains("activated", result);
        _dataverse.Verify(c => c.ActivateWorkflowAsync(Wf, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(state.OperationStack);
        Assert.Equal("DeactivateWorkflow", state.OperationStack.Peek().ReverseOp!.ToolName);
    }

    [Fact]
    public async Task Activate_AlreadyActive_NoOp()
    {
        SetupSession("act-2", MakeWorkflow("Rule X", activated: true));
        var tool = new ActivateWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<ActivateWorkflowTool>.Instance);

        var result = await tool.ActivateWorkflowAsync(Wf, CancellationToken.None);

        Assert.StartsWith("NO_OP:", result);
        _dataverse.Verify(c => c.ActivateWorkflowAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Activate_NotFound_NoOp()
    {
        SetupSession("act-3", null);
        var tool = new ActivateWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<ActivateWorkflowTool>.Instance);

        var result = await tool.ActivateWorkflowAsync(Wf, CancellationToken.None);

        Assert.StartsWith("NO_OP:", result);
    }

    // ── Deactivate ──────────────────────────────────────────────────────

    [Fact]
    public async Task Deactivate_Active_FiresApi()
    {
        var state = SetupSession("deact-1", MakeWorkflow("BPF", activated: true));
        _dataverse.Setup(c => c.DeactivateWorkflowAsync(Wf, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tool = new DeactivateWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<DeactivateWorkflowTool>.Instance);

        var result = await tool.DeactivateWorkflowAsync(Wf, CancellationToken.None);

        Assert.Contains("deactivated", result);
        _dataverse.Verify(c => c.DeactivateWorkflowAsync(Wf, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("ActivateWorkflow", state.OperationStack.Peek().ReverseOp!.ToolName);
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivated_NoOp()
    {
        SetupSession("deact-2", MakeWorkflow("BPF", activated: false));
        var tool = new DeactivateWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<DeactivateWorkflowTool>.Instance);

        var result = await tool.DeactivateWorkflowAsync(Wf, CancellationToken.None);

        Assert.StartsWith("NO_OP:", result);
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_HappyPath_FiresApiAndAudits()
    {
        var state = SetupSession("del-1", MakeWorkflow("Rule X", activated: false));
        _dataverse.Setup(c => c.DeleteWorkflowAsync(Wf, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var tool = new DeleteWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<DeleteWorkflowTool>.Instance);

        var result = await tool.DeleteWorkflowAsync(Wf, CancellationToken.None);

        Assert.Contains("deleted", result);
        Assert.Single(state.OperationStack);
        Assert.False(state.OperationStack.Peek().IsReversible);
    }

    [Fact]
    public async Task Delete_Activated_Refuses()
    {
        SetupSession("del-2", MakeWorkflow("Rule X", activated: true));
        var tool = new DeleteWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<DeleteWorkflowTool>.Instance);

        var result = await tool.DeleteWorkflowAsync(Wf, CancellationToken.None);

        Assert.StartsWith("REFUSED:", result);
        Assert.Contains("deactivate_workflow", result);
        _dataverse.Verify(c => c.DeleteWorkflowAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_Managed_Refuses()
    {
        SetupSession("del-3", MakeWorkflow("Rule X", activated: false, managed: true));
        var tool = new DeleteWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<DeleteWorkflowTool>.Instance);

        var result = await tool.DeleteWorkflowAsync(Wf, CancellationToken.None);

        Assert.StartsWith("REFUSED:", result);
        Assert.Contains("managed", result);
        _dataverse.Verify(c => c.DeleteWorkflowAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_NotFound_NoOp()
    {
        SetupSession("del-4", null);
        var tool = new DeleteWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<DeleteWorkflowTool>.Instance);

        var result = await tool.DeleteWorkflowAsync(Wf, CancellationToken.None);

        Assert.StartsWith("NO_OP:", result);
    }

    // ── Update (rename) plan + execute ──────────────────────────────────

    [Fact]
    public async Task PlanUpdate_RenameOnly_StagesPatchPayload()
    {
        var state = SetupSession("upd-1", MakeWorkflow("Old Name", activated: false));
        var planner = new PlanUpdateWorkflowTool(_store.Object, _ctx.Object, _dataverse.Object);

        var plan = await planner.PlanUpdateWorkflowAsync(Wf, "New Name", "", CancellationToken.None);

        Assert.StartsWith("update_business_rule_", plan.PlanId);
        Assert.True(state.PendingPlans.ContainsKey(plan.PlanId));
        Assert.Contains("\"name\":\"New Name\"", state.PendingPlans[plan.PlanId]);
    }

    [Fact]
    public async Task PlanUpdate_NoOpEmptyInputs_Rejects()
    {
        SetupSession("upd-2", MakeWorkflow("Old Name", activated: false));
        var planner = new PlanUpdateWorkflowTool(_store.Object, _ctx.Object, _dataverse.Object);

        var plan = await planner.PlanUpdateWorkflowAsync(Wf, "", "", CancellationToken.None);

        Assert.StartsWith("rejected_update_workflow_invalid_", plan.PlanId);
        Assert.Contains(plan.Discrepancies, d => d.Actual!.Contains("No-op"));
    }

    [Fact]
    public async Task PlanUpdate_ManagedWorkflow_Rejects()
    {
        SetupSession("upd-3", MakeWorkflow("Mgd", activated: false, managed: true));
        var planner = new PlanUpdateWorkflowTool(_store.Object, _ctx.Object, _dataverse.Object);

        var plan = await planner.PlanUpdateWorkflowAsync(Wf, "Rename Attempt", "", CancellationToken.None);

        Assert.StartsWith("rejected_update_workflow_managed_", plan.PlanId);
    }

    [Fact]
    public async Task ExecuteUpdate_HappyPath_CallsPatch()
    {
        var planId = "update_business_rule_smoke";
        var state = new ProjectState { ProjectId = "exec-upd-1" };
        var composite = $$"""
            {
              "workflowId": "{{Wf}}",
              "name": "Renamed",
              "primaryentity": "account",
              "category": 2,
              "patch": { "name": "Renamed" }
            }
            """;
        state.PendingPlans[planId] = composite;
        _ctx.Setup(c => c.SessionId).Returns("exec-upd-1");
        _store.Setup(s => s.GetOrCreateAsync("exec-upd-1", It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _store.Setup(s => s.SaveAsync("exec-upd-1", It.IsAny<ProjectState>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _dataverse.Setup(c => c.UpdateWorkflowAsync(Wf, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var tool = new ExecuteUpdateWorkflowTool(_dataverse.Object, _store.Object, _ctx.Object,
            NullLogger<ExecuteUpdateWorkflowTool>.Instance);

        var result = await tool.ExecuteUpdateWorkflowAsync(planId, CancellationToken.None);

        Assert.Contains("updated", result);
        _dataverse.Verify(c => c.UpdateWorkflowAsync(Wf, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(state.ExecutedPlans.ContainsKey(planId));
    }
}
