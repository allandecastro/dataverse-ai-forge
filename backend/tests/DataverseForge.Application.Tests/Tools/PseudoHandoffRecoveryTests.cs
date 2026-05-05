using DataverseForge.Application.Agents;
using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class PseudoHandoffRecoveryTests
{
    private static (Mock<IProjectStateStore> store, Mock<ISessionContext> ctx, ProjectState state) BuildHarness(string sessionId = "test-session")
    {
        var state = new ProjectState
        {
            ProjectId = sessionId,
            ActiveAgentName = AgentNames.UIBuilder,
        };
        var store = new Mock<IProjectStateStore>();
        store.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        store.Setup(s => s.SaveAsync(sessionId, It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var ctx = new Mock<ISessionContext>();
        ctx.SetupGet(c => c.SessionId).Returns(sessionId);
        ctx.SetupGet(c => c.RunId).Returns("run-1");
        return (store, ctx, state);
    }

    [Fact]
    public async Task NormalProse_LeftUnchanged()
    {
        var (store, ctx, _) = BuildHarness();
        var input = "Handing this to the Solution Architect — they handle data work.";
        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Same(input, output);
        store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EmptyText_LeftUnchanged()
    {
        var (store, ctx, _) = BuildHarness();
        var output = await PseudoHandoffRecovery.RecoverAsync(
            "", store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);
        Assert.Equal("", output);
    }

    [Fact]
    public async Task PseudoHandoffWithRequirementSpec_FiresRealHandoff()
    {
        // The exact pattern from the 2026-05-05 trace — UIBuilder emitted this
        // as its entire reply, no real tool call attached.
        var (store, ctx, state) = BuildHarness();
        var input = "handoff_to_solution_architect(requirementSpec=\"User requested to create 5 sample gift records; data population is out of UI building scope.\")";

        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.NotEqual(input, output);
        Assert.Contains("Solution Architect", output, StringComparison.Ordinal);
        Assert.DoesNotContain("(", output, StringComparison.Ordinal);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);
    }

    [Fact]
    public async Task PseudoHandoffWithReasonAlias_FiresRealHandoff()
    {
        var (store, ctx, state) = BuildHarness();
        var input = "handoff_to_solution_architect(reason=\"User requested data work, out of scope.\")";

        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.NotEqual(input, output);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);
    }

    [Fact]
    public async Task PseudoHandoffToDataPopulation_RoutesToDataPopulation()
    {
        var (store, ctx, state) = BuildHarness();
        var input = "handoff_to_data_population(context=\"Create 5 sample gift records.\")";

        await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(AgentNames.DataPopulation, state.ActiveAgentName);
    }

    [Fact]
    public async Task UnknownTargetName_LeftUnchanged()
    {
        // We don't recognize the target; safer to leave the text as-is than
        // route to a wrong agent.
        var (store, ctx, state) = BuildHarness();
        var input = "handoff_to_some_made_up_agent(reason=\"foo\")";

        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(input, output);
        Assert.Equal(AgentNames.UIBuilder, state.ActiveAgentName); // unchanged
    }

    [Fact]
    public async Task ProseWithEmbeddedFunctionMention_LeftUnchanged()
    {
        // A normal sentence that happens to mention a function name — must NOT
        // trigger a handoff because we can't be sure the user wanted one.
        var (store, ctx, state) = BuildHarness();
        var input = "I'll call handoff_to_solution_architect(requirementSpec) next.";

        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(input, output);
        Assert.Equal(AgentNames.UIBuilder, state.ActiveAgentName);
    }

    [Fact]
    public async Task PseudoHandoff_WhitespaceTolerated()
    {
        var (store, ctx, state) = BuildHarness();
        var input = "\n  handoff_to_solution_architect(requirementSpec=\"foo\")  \n";

        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.NotEqual(input, output);
        Assert.Equal(AgentNames.SolutionArchitect, state.ActiveAgentName);
    }
}
