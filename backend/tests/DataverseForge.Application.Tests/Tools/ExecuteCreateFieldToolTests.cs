using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class ExecuteCreateFieldToolTests
{
    private const string PlanId = "create_field_test";
    private const string PayloadJson = "{\"SchemaName\":\"cr123_Priority\"}";
    private const string TableLogical = "cr123_workorder";

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private ExecuteCreateFieldTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object, NullLogger<ExecuteCreateFieldTool>.Instance);

    private (ProjectState state, Guid tableId) SetupDefaults(string sessionId, bool seedTable = true)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.PendingPlans[PlanId] = PayloadJson;
        var tableId = Guid.NewGuid();
        if (seedTable)
        {
            state.Tables.Add(new CreatedTable(tableId, TableLogical, "Work Order", DateTime.UtcNow));
        }

        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        _mockDataverseClient
            .Setup(c => c.CreateFieldAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        return (state, tableId);
    }

    [Fact]
    public async Task ExecuteCreateField_ResolvesTableFromState_AndCallsCreateField()
    {
        var (_, tableId) = SetupDefaults("exec-field-test-1");
        var tool = CreateTool();

        await tool.ExecuteCreateFieldAsync(
            PlanId, TableLogical,
            "cr123_priority", "Priority", "localOptionSet", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateFieldAsync(tableId, PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteCreateField_AddsToStateFields_AndRemovesPlan()
    {
        var (state, _) = SetupDefaults("exec-field-test-2");
        var tool = CreateTool();

        await tool.ExecuteCreateFieldAsync(
            PlanId, TableLogical,
            "cr123_priority", "Priority", "localOptionSet", CancellationToken.None);

        Assert.Single(state.Fields);
        Assert.Equal("cr123_priority", state.Fields[0].LogicalName);
        Assert.Equal(TableLogical, state.Fields[0].TableLogicalName);
        Assert.Equal("localOptionSet", state.Fields[0].FieldType);
        Assert.False(state.PendingPlans.ContainsKey(PlanId));
    }

    [Fact]
    public async Task ExecuteCreateField_PushesToOperationStack()
    {
        var (state, _) = SetupDefaults("exec-field-test-3");
        var tool = CreateTool();

        await tool.ExecuteCreateFieldAsync(
            PlanId, TableLogical,
            "cr123_priority", "Priority", "localOptionSet", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("ExecuteCreateField", op.ToolName);
        Assert.True(op.IsReversible);
    }

    [Fact]
    public async Task ExecuteCreateField_ReturnsError_WhenPlanIdMissing()
    {
        var (state, _) = SetupDefaults("exec-field-test-4");
        state.PendingPlans.Clear();
        var tool = CreateTool();

        var result = await tool.ExecuteCreateFieldAsync(
            "missing", TableLogical,
            "cr123_priority", "Priority", "string", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.CreateFieldAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateField_RejectedExistsPlan_ReturnsSkippedAndNeverCallsDataverse()
    {
        // Companion to PlanCreateFieldTool's exact-match rejection path. If the
        // agent ignores the rejection and tries to execute, this guard returns a
        // SKIPPED result that won't be misread as a transient failure.
        var sessionId = "exec-field-rejected";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.ExecuteCreateFieldAsync(
            planId: $"rejected_field_exists_{Guid.NewGuid():N}",
            tableLogicalName: TableLogical,
            fieldLogicalName: "cr123_description",
            displayName: "Description",
            fieldType: "memo",
            cancellationToken: CancellationToken.None);

        Assert.StartsWith("SKIPPED:", result);
        Assert.Contains("already exists", result);
        Assert.Contains("DO NOT retry", result);
        _mockDataverseClient.Verify(
            c => c.CreateFieldAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteCreateField_FallsBackToLiveLookup_WhenTableNotInState()
    {
        // No table seeded in state — tool must fall back to live EntityDefinitions
        // lookup. Verifies we never depend on a (potentially hallucinated) GUID.
        var (state, _) = SetupDefaults("exec-field-test-live", seedTable: false);
        var liveTableId = Guid.NewGuid();
        _mockDataverseClient
            .Setup(c => c.GetTableMetadataAsync(TableLogical, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TableMetadata(liveTableId, TableLogical, "Work Order", "cr123_WorkOrder", "UserOwned", false, false, false, false));

        var tool = CreateTool();

        await tool.ExecuteCreateFieldAsync(
            PlanId, TableLogical,
            "cr123_priority", "Priority", "string", CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.CreateFieldAsync(liveTableId, PayloadJson, It.IsAny<CancellationToken>()), Times.Once);
        // The live-resolved table should now be cached in state.
        Assert.Contains(state.Tables, t => t.MetadataId == liveTableId && t.LogicalName == TableLogical);
    }

    [Fact]
    public async Task ExecuteCreateField_ReturnsError_WhenTableUnresolvable()
    {
        // Neither in state nor in Dataverse — agent passed a wrong logical name.
        // Tool must NOT silently proceed with a fabricated GUID; it must error.
        SetupDefaults("exec-field-test-unresolvable", seedTable: false);
        _mockDataverseClient
            .Setup(c => c.GetTableMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404"));

        var tool = CreateTool();

        var result = await tool.ExecuteCreateFieldAsync(
            PlanId, TableLogical,
            "cr123_priority", "Priority", "string", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        _mockDataverseClient.Verify(
            c => c.CreateFieldAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
