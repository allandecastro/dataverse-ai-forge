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
        // A reply that doesn't open with a handoff intent — should NOT
        // trigger recovery. (The narrative path requires the message to
        // start with a "handing off to X" phrase.)
        var (store, ctx, _) = BuildHarness();
        var input = "The Solution Architect will pick up the data work next.";
        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Same(input, output);
        store.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ProjectState>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NarrativeHandoff_FiresRealHandoff()
    {
        // Real session 2026-05-06 18:05: SA wrote "Handing off to UI Builder
        // to add the Account, Contact, and eppc_gift tables to the Phase3
        // Verify app navigation" with no actual handoff_to_ui_builder tool
        // call. The function-call regex misses this — narrative path catches it.
        var (store, ctx, state) = BuildHarness();
        var input = "Handing off to UI Builder to add the Account, Contact, and eppc_gift tables to the Phase3 Verify app navigation. I'll update you once this is done.";

        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.NotEqual(input, output);
        Assert.Equal(AgentNames.UIBuilder, state.ActiveAgentName);
    }

    [Fact]
    public async Task NarrativeHandoff_WithLeadingPronoun_FiresRealHandoff()
    {
        // Variant: "I will hand this off to the Data Model Builder…"
        var (store, ctx, state) = BuildHarness();
        var input = "I will hand this off to the Data Model Builder to add the choice field.";

        await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(AgentNames.DataModelBuilder, state.ActiveAgentName);
    }

    [Fact]
    public async Task NarrativeHandoffToSecurityBuilder_FiresRealHandoff()
    {
        // 2026-05-13 real session: SA wrote "Handing off to Security Builder
        // to create the 'Forge G2 Reviewer' security role..." with no actual
        // tool call. SB / AB were missing from the regex when those agents
        // shipped, so the narrative recovery never fired and SA would
        // re-emit the same phrase turn after turn.
        var (store, ctx, state) = BuildHarness();
        var input = "Handing off to Security Builder to create the 'Forge G2 Reviewer' security role with Read on eppc_gift at BU depth.";

        await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(AgentNames.SecurityBuilder, state.ActiveAgentName);
    }

    [Fact]
    public async Task NarrativeHandoffToAutomationBuilder_FiresRealHandoff()
    {
        // Same regression class as SecurityBuilder — AutomationBuilder
        // narrative handoffs were silently dropped. 2026-05-13 session had
        // SA emit "Handing off to Automation Builder to activate the
        // Business Rule..." five times in a row because the recovery never
        // fired and the actual tool call wasn't being emitted by the model.
        var (store, ctx, state) = BuildHarness();
        var input = "Handing off to Automation Builder to activate the Business Rule 'TEST G3 Renamed' on the eppc_gift table.";

        await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(AgentNames.AutomationBuilder, state.ActiveAgentName);
    }

    [Fact]
    public async Task NarrativeHandoffMidMessage_DoesNotFire()
    {
        // The handoff phrase appears mid-message, not at the start. We must
        // NOT fire — the agent might be describing future plans, not
        // committing to an immediate handoff.
        var (store, ctx, state) = BuildHarness();
        var input = "First I'll review the schema, then I'll hand off to UI Builder for the form work.";

        var output = await PseudoHandoffRecovery.RecoverAsync(
            input, store.Object, ctx.Object, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(input, output);
        Assert.Equal(AgentNames.UIBuilder, state.ActiveAgentName); // unchanged from harness default
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
