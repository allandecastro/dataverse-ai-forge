using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class AddExistingTableToSolutionToolTests
{
    private const SolutionComponentType EntityComponentType = SolutionComponentType.Entity;

    private static (Mock<IDataverseClient> Client, Mock<IProjectStateStore> Store, Mock<ISessionContext> Ctx, ProjectState State) Setup(
        string? solutionUniqueName = "MyAppSol",
        string solutionDisplayName = "My App")
    {
        var state = new ProjectState
        {
            SolutionUniqueName = solutionUniqueName ?? string.Empty,
            SolutionDisplayName = solutionDisplayName,
        };
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns("test-session");
        return (new Mock<IDataverseClient>(), store, ctx, state);
    }

    private static AddExistingTableToSolutionTool MakeTool(Mock<IDataverseClient> client,
        Mock<IProjectStateStore> store, Mock<ISessionContext> ctx) =>
        new(client.Object, store.Object, ctx.Object,
            NullLogger<AddExistingTableToSolutionTool>.Instance);

    private static TableMetadata Meta(Guid? id = null, string logical = "account", string display = "Account") =>
        new(id ?? Guid.NewGuid(), logical, display, "Account", "UserOwned", true, true, false, true);

    [Fact]
    public async Task ReturnsError_WhenNoActiveSolution()
    {
        var (client, store, ctx, _) = Setup(solutionUniqueName: null);
        var tool = MakeTool(client, store, ctx);

        var result = await tool.AddExistingTableToSolutionAsync("account", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("No active solution", result);
        client.Verify(c => c.AddSolutionComponentAsync(
            It.IsAny<Guid>(), It.IsAny<SolutionComponentType>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReturnsError_WhenTableHasNoMetadataId()
    {
        var (client, store, ctx, _) = Setup();
        client.Setup(c => c.GetTableMetadataAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meta(id: Guid.Empty));
        var tool = MakeTool(client, store, ctx);

        var result = await tool.AddExistingTableToSolutionAsync("account", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("no MetadataId", result);
        client.Verify(c => c.AddSolutionComponentAsync(
            It.IsAny<Guid>(), It.IsAny<SolutionComponentType>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HappyPath_CallsAddSolutionComponent_WithEntityComponentType()
    {
        var (client, store, ctx, _) = Setup();
        var metadataId = Guid.NewGuid();
        client.Setup(c => c.GetTableMetadataAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meta(id: metadataId));
        client.Setup(c => c.AddSolutionComponentAsync(
                metadataId, EntityComponentType, "MyAppSol", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = MakeTool(client, store, ctx);
        var result = await tool.AddExistingTableToSolutionAsync("account", CancellationToken.None);

        Assert.Contains("Added the", result);
        Assert.Contains("table", result);
        Assert.Contains("TABLE ONLY", result);
        Assert.Contains("Account", result);
        Assert.Contains("My App", result);
        client.Verify(c => c.AddSolutionComponentAsync(
            metadataId, EntityComponentType, "MyAppSol", It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LowercasesTableLogicalName_BeforeMetadataLookup()
    {
        // Symptom: agent passes "Account" (capitalized); Dataverse logical names are
        // case-sensitive lowercase, so we must normalize before the lookup.
        var (client, store, ctx, _) = Setup();
        client.Setup(c => c.GetTableMetadataAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meta(id: Guid.NewGuid()));

        var tool = MakeTool(client, store, ctx);
        await tool.AddExistingTableToSolutionAsync("Account", CancellationToken.None);

        client.Verify(c => c.GetTableMetadataAsync("account", It.IsAny<CancellationToken>()), Times.Once);
        client.Verify(c => c.GetTableMetadataAsync("Account", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReturnsError_WhenAddSolutionComponentThrows()
    {
        var (client, store, ctx, _) = Setup();
        client.Setup(c => c.GetTableMetadataAsync("contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meta(id: Guid.NewGuid(), logical: "contact", display: "Contact"));
        client.Setup(c => c.AddSolutionComponentAsync(
                It.IsAny<Guid>(), EntityComponentType, "MyAppSol", It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 Forbidden"));

        var tool = MakeTool(client, store, ctx);
        var result = await tool.AddExistingTableToSolutionAsync("contact", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("contact", result);
        Assert.Contains("403 Forbidden", result);
    }

    [Fact]
    public async Task ReturnsError_WhenMetadataLookupFails()
    {
        var (client, store, ctx, _) = Setup();
        client.Setup(c => c.GetTableMetadataAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Table not found"));

        var tool = MakeTool(client, store, ctx);
        var result = await tool.AddExistingTableToSolutionAsync("nonexistent", CancellationToken.None);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("nonexistent", result);
        client.Verify(c => c.AddSolutionComponentAsync(
            It.IsAny<Guid>(), It.IsAny<SolutionComponentType>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HappyPath_PushesReversibleOperationOntoStack()
    {
        // Symptom this protects: undo not working for solution-membership changes
        // because the tool wasn't pushing to the OperationStack at all.
        var (client, store, ctx, state) = Setup();
        var metadataId = Guid.NewGuid();
        client.Setup(c => c.GetTableMetadataAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meta(id: metadataId));

        var tool = MakeTool(client, store, ctx);
        await tool.AddExistingTableToSolutionAsync("account", CancellationToken.None);

        Assert.Single(state.OperationStack);
        var op = state.OperationStack.Peek();
        Assert.Equal("AddExistingTableToSolution", op.ToolName);
        Assert.True(op.IsReversible);
        Assert.Equal("RemoveSolutionComponent", op.ReverseOp?.ToolName);
    }

    [Fact]
    public async Task HappyPath_AddsAuditLogEntry()
    {
        var (client, store, ctx, state) = Setup();
        client.Setup(c => c.GetTableMetadataAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meta(id: Guid.NewGuid()));

        var tool = MakeTool(client, store, ctx);
        await tool.AddExistingTableToSolutionAsync("account", CancellationToken.None);

        Assert.Single(state.AuditLog);
        var entry = state.AuditLog[0];
        Assert.Equal("AddExistingTableToSolution", entry.Action);
        Assert.True(entry.Success);
        Assert.Contains("account", entry.Detail);
    }

    [Fact]
    public async Task ErrorPath_DoesNotPushToOperationStack()
    {
        // Failed operations must not pollute the undo stack.
        var (client, store, ctx, state) = Setup();
        client.Setup(c => c.GetTableMetadataAsync("contact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Meta(id: Guid.NewGuid(), logical: "contact", display: "Contact"));
        client.Setup(c => c.AddSolutionComponentAsync(
                It.IsAny<Guid>(), It.IsAny<SolutionComponentType>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("403 Forbidden"));

        var tool = MakeTool(client, store, ctx);
        await tool.AddExistingTableToSolutionAsync("contact", CancellationToken.None);

        Assert.Empty(state.OperationStack);
    }
}
