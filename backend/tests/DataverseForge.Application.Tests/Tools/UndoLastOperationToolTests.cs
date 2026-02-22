using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class UndoLastOperationToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private UndoLastOperationTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupDefaults(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore
            .Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task EmptyStack_ReturnsMessage()
    {
        var sessionId = "undo-test-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.UndoLastOperationAsync(true, CancellationToken.None);

        Assert.Contains("empty", result);
    }

    [Fact]
    public async Task NonReversibleOperation_ReturnsNotReversible()
    {
        var sessionId = "undo-test-2";
        var state = SetupDefaults(sessionId);
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "SomeNonReversibleTool",
            IsReversible = false,
            ReverseOp = null
        });
        var tool = CreateTool();

        var result = await tool.UndoLastOperationAsync(true, CancellationToken.None);

        Assert.Contains("not reversible", result);
    }

    [Fact]
    public async Task WithoutConfirmation_ReturnsPreview()
    {
        var sessionId = "undo-test-3";
        var state = SetupDefaults(sessionId);
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateTable",
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteTable",
                Parameters = new { metadataId = Guid.NewGuid(), tableLogicalName = "cr123_test" },
                Warning = "Deleting a table removes all data.",
                RiskLevel = RiskLevel.High
            }
        });
        var tool = CreateTool();

        var result = await tool.UndoLastOperationAsync(false, CancellationToken.None);

        Assert.Contains("Undo Preview", result);
        Assert.Contains("ExecuteCreateTable", result);
        Assert.Contains("DeleteTable", result);
        Assert.Contains("High", result);
    }

    [Fact]
    public async Task WithConfirmation_DeletesTable_PopsStack()
    {
        var sessionId = "undo-test-4";
        var state = SetupDefaults(sessionId);
        var metadataId = Guid.NewGuid();
        state.Tables.Add(new CreatedTable(metadataId, "cr123_test", "Test", DateTime.UtcNow));
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateTable",
            Result = new { metadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteTable",
                Parameters = new { metadataId, tableLogicalName = "cr123_test" },
                Warning = "Deleting a table.",
                RiskLevel = RiskLevel.High
            }
        });
        var tool = CreateTool();

        var result = await tool.UndoLastOperationAsync(true, CancellationToken.None);

        Assert.Contains("Successfully undone", result);
        Assert.Empty(state.OperationStack);
        Assert.Empty(state.Tables);
        _mockDataverseClient.Verify(
            c => c.DeleteTableAsync(metadataId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WithConfirmation_AddsAuditEntry()
    {
        var sessionId = "undo-test-5";
        var state = SetupDefaults(sessionId);
        var metadataId = Guid.NewGuid();
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateTable",
            Result = new { metadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteTable",
                Parameters = new { metadataId, tableLogicalName = "cr123_test" },
                Warning = "Warning",
                RiskLevel = RiskLevel.High
            }
        });
        var tool = CreateTool();

        await tool.UndoLastOperationAsync(true, CancellationToken.None);

        Assert.Single(state.AuditLog);
        Assert.True(state.AuditLog[0].Success);
        Assert.Contains("Undo", state.AuditLog[0].Action);
    }

    [Fact]
    public async Task FailedUndo_LogsErrorAuditEntry()
    {
        var sessionId = "undo-test-6";
        var state = SetupDefaults(sessionId);
        var metadataId = Guid.NewGuid();
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateTable",
            Result = new { metadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteTable",
                Parameters = new { metadataId, tableLogicalName = "cr123_test" },
                Warning = "Warning",
                RiskLevel = RiskLevel.High
            }
        });
        _mockDataverseClient
            .Setup(c => c.DeleteTableAsync(metadataId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("API error"));
        var tool = CreateTool();

        var result = await tool.UndoLastOperationAsync(true, CancellationToken.None);

        Assert.Contains("Failed to undo", result);
        Assert.Single(state.OperationStack); // Not popped
        Assert.Single(state.AuditLog);
        Assert.False(state.AuditLog[0].Success);
    }

    [Fact]
    public async Task SavesStateAfterUndo()
    {
        var sessionId = "undo-test-7";
        var state = SetupDefaults(sessionId);
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateTable",
            Result = new { metadataId = Guid.NewGuid() },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteTable",
                Parameters = new { metadataId = Guid.NewGuid(), tableLogicalName = "cr123_test" },
                Warning = "Warning",
                RiskLevel = RiskLevel.High
            }
        });
        var tool = CreateTool();

        await tool.UndoLastOperationAsync(true, CancellationToken.None);

        _mockProjectStateStore.Verify(
            s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListUndoableOperations_EmptyStack()
    {
        var sessionId = "undo-list-1";
        SetupDefaults(sessionId);
        var tool = CreateTool();

        var result = await tool.ListUndoableOperationsAsync(5, CancellationToken.None);

        Assert.Contains("empty", result);
    }

    [Fact]
    public async Task ListUndoableOperations_ShowsEntries()
    {
        var sessionId = "undo-list-2";
        var state = SetupDefaults(sessionId);
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateTable",
            IsReversible = true,
            ReverseOp = new ReverseOperation { ToolName = "DeleteTable", Warning = "", RiskLevel = RiskLevel.High }
        });
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op2",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.UIBuilder,
            ToolName = "ExecuteCreateForm",
            IsReversible = true,
            ReverseOp = new ReverseOperation { ToolName = "DeleteForm", Warning = "", RiskLevel = RiskLevel.Medium }
        });
        var tool = CreateTool();

        var result = await tool.ListUndoableOperationsAsync(5, CancellationToken.None);

        Assert.Contains("Operation Stack", result);
        Assert.Contains("2 total", result);
        Assert.Contains("ExecuteCreateForm", result);
        Assert.Contains("NEXT TO UNDO", result);
    }

    [Fact]
    public async Task UndoDeleteField_CallsDeleteFieldAsync()
    {
        var sessionId = "undo-field-1";
        var state = SetupDefaults(sessionId);
        var tableMetadataId = Guid.NewGuid();
        var fieldMetadataId = Guid.NewGuid();
        state.Fields.Add(new CreatedField(fieldMetadataId, "cr123_test", "cr123_name", "Name", "String", DateTime.UtcNow));
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            ToolName = "ExecuteCreateField",
            Result = new { metadataId = fieldMetadataId },
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "DeleteField",
                Parameters = new { tableMetadataId, fieldMetadataId, fieldLogicalName = "cr123_name" },
                Warning = "Deleting a field.",
                RiskLevel = RiskLevel.High
            }
        });
        var tool = CreateTool();

        await tool.UndoLastOperationAsync(true, CancellationToken.None);

        _mockDataverseClient.Verify(
            c => c.DeleteFieldAsync(tableMetadataId, fieldMetadataId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(state.Fields);
    }
}
