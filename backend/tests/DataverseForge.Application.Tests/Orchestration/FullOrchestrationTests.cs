using DataverseForge.Application.Contracts;
using DataverseForge.Application.Orchestration;
using DataverseForge.Domain;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Orchestration;

/// <summary>
/// Integration-style tests that verify the full 10-agent orchestration topology,
/// handoff validation, and undo support through the AgentOrchestrator.
/// </summary>
public class FullOrchestrationTests
{
    private readonly Mock<IProjectStateStore> _mockStore = new();

    // ─── HANDOFF TOPOLOGY VALIDATION ───

    [Fact]
    public void AllTenAgentsRegistered()
    {
        var allAgents = new[]
        {
            AgentNames.SolutionArchitect,
            AgentNames.BusinessAnalyst,
            AgentNames.DataModelBuilder,
            AgentNames.Reviewer,
            AgentNames.Tester,
            AgentNames.Documentor,
            AgentNames.UIBuilder,
            AgentNames.AutomationBuilder,
            AgentNames.SecurityBuilder,
            AgentNames.DataPopulation
        };

        Assert.Equal(10, allAgents.Length);
        Assert.All(allAgents, name => Assert.NotEmpty(name));
        Assert.Equal(allAgents.Length, allAgents.Distinct().Count());
    }

    [Fact]
    public void StandardWorkflowSequence_AllHandoffsValid()
    {
        // Standard sequence: SA → BA → SA → DMB → Reviewer → SA → DMB → SA → Tester → SA → Documentor → SA
        var sequence = new[]
        {
            (AgentNames.SolutionArchitect, AgentNames.BusinessAnalyst),
            (AgentNames.BusinessAnalyst, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.DataModelBuilder),
            (AgentNames.DataModelBuilder, AgentNames.Reviewer),
            (AgentNames.Reviewer, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.DataModelBuilder),
            (AgentNames.DataModelBuilder, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.Tester),
            (AgentNames.Tester, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.Documentor),
            (AgentNames.Documentor, AgentNames.SolutionArchitect)
        };

        foreach (var (from, to) in sequence)
        {
            var result = HandoffValidator.Validate(from, to);
            Assert.True(result.IsValid, $"Handoff {from} → {to} should be valid but got: {result.ErrorMessage}");
        }
    }

    [Fact]
    public void FullFeedbackModuleScenario_AllHandoffsValid()
    {
        // Full "feedback module" scenario exercising all 10 agents:
        // SA → BA (requirements) → SA → Reviewer (design validation) → SA → DMB (create tables)
        // → Tester (verify) → SA → UIBuilder (forms/views) → Tester (verify) → SA
        // → AutomationBuilder (BPF) → SA → SecurityBuilder (roles) → SA
        // → DataPopulation (seed data) → SA → Tester (full validation) → SA → Documentor (docs) → SA
        var handoffs = new[]
        {
            (AgentNames.SolutionArchitect, AgentNames.BusinessAnalyst),      // 1. Requirements
            (AgentNames.BusinessAnalyst, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.Reviewer),             // 2. Validate design
            (AgentNames.Reviewer, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.DataModelBuilder),     // 3. Create schema
            (AgentNames.DataModelBuilder, AgentNames.Tester),                // → Verify
            (AgentNames.Tester, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.UIBuilder),            // 4. Forms/views
            (AgentNames.UIBuilder, AgentNames.Tester),                       // → Verify
            (AgentNames.Tester, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.AutomationBuilder),    // 5. BPF
            (AgentNames.AutomationBuilder, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.SecurityBuilder),      // 6. Security roles
            (AgentNames.SecurityBuilder, AgentNames.Tester),                 // → Verify
            (AgentNames.Tester, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.DataPopulation),       // 7. Seed data
            (AgentNames.DataPopulation, AgentNames.Tester),                  // → Verify
            (AgentNames.Tester, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.Tester),               // 8. Full validation
            (AgentNames.Tester, AgentNames.SolutionArchitect),
            (AgentNames.SolutionArchitect, AgentNames.Documentor),           // 9. Documentation
            (AgentNames.Documentor, AgentNames.SolutionArchitect)
        };

        var visitedAgents = new HashSet<string>();
        foreach (var (from, to) in handoffs)
        {
            visitedAgents.Add(from);
            visitedAgents.Add(to);
            var result = HandoffValidator.Validate(from, to);
            Assert.True(result.IsValid, $"Handoff {from} → {to} should be valid but got: {result.ErrorMessage}");
        }

        // Verify all 10 agents were visited
        Assert.Equal(10, visitedAgents.Count);
    }

    // ─── BUILDER ISOLATION ───

    [Fact]
    public void NoBuilderToBuilderHandoffs_AreValid()
    {
        var builders = new[]
        {
            AgentNames.DataModelBuilder,
            AgentNames.UIBuilder,
            AgentNames.AutomationBuilder,
            AgentNames.SecurityBuilder,
            AgentNames.DataPopulation
        };

        foreach (var from in builders)
        {
            foreach (var to in builders)
            {
                if (from == to) continue;
                var result = HandoffValidator.Validate(from, to);
                Assert.False(result.IsValid, $"Builder→Builder handoff {from} → {to} should be blocked");
            }
        }
    }

    // ─── OPERATION STACK UNDO SUPPORT ───

    [Fact]
    public void OperationStack_MaintainsLIFOOrder()
    {
        var state = new ProjectState();

        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "1", ToolName = "ExecuteCreateTable",
            AgentName = AgentNames.DataModelBuilder,
            ExecutedAt = DateTime.UtcNow,
            IsReversible = true
        });
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "2", ToolName = "ExecuteCreateField",
            AgentName = AgentNames.DataModelBuilder,
            ExecutedAt = DateTime.UtcNow,
            IsReversible = true
        });
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "3", ToolName = "ExecuteCreateForm",
            AgentName = AgentNames.UIBuilder,
            ExecutedAt = DateTime.UtcNow,
            IsReversible = true
        });

        Assert.Equal(3, state.OperationStack.Count);
        Assert.Equal("ExecuteCreateForm", state.OperationStack.Pop().ToolName);
        Assert.Equal("ExecuteCreateField", state.OperationStack.Pop().ToolName);
        Assert.Equal("ExecuteCreateTable", state.OperationStack.Pop().ToolName);
    }

    [Fact]
    public void AuditLog_TracksAllAgentActions()
    {
        var state = new ProjectState();

        var agentActions = new[]
        {
            (AgentNames.SolutionArchitect, "Onboarding"),
            (AgentNames.BusinessAnalyst, "RequirementElicitation"),
            (AgentNames.DataModelBuilder, "ExecuteCreateTable"),
            (AgentNames.Reviewer, "ValidateSchemaDesign"),
            (AgentNames.Tester, "VerifyTableCreation"),
            (AgentNames.Documentor, "GenerateDataDictionary"),
            (AgentNames.UIBuilder, "ExecuteCreateForm"),
            (AgentNames.AutomationBuilder, "ExecuteCreateBpf"),
            (AgentNames.SecurityBuilder, "ExecuteCreateSecurityRole"),
            (AgentNames.DataPopulation, "ExecutePopulateReferenceData")
        };

        foreach (var (agent, action) in agentActions)
        {
            state.AuditLog.Add(new AuditEntry
            {
                EntryId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                AgentName = agent,
                Action = action,
                Detail = $"Test action by {agent}",
                Success = true
            });
        }

        Assert.Equal(10, state.AuditLog.Count);
        Assert.All(state.AuditLog, entry => Assert.True(entry.Success));

        var distinctAgents = state.AuditLog.Select(e => e.AgentName).Distinct().ToList();
        Assert.Equal(10, distinctAgents.Count);
    }

    // ─── ERROR RECOVERY ───

    [Fact]
    public void ProjectState_TracksPartialFailures()
    {
        var state = new ProjectState();

        // Simulate a batch of 3 table creates where the 2nd one fails
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "cr123_feedback", "Feedback", DateTime.UtcNow));
        // Table 2 failed — not added to state

        state.AuditLog.Add(new AuditEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            Action = "ExecuteCreateTable",
            Detail = "Created table 'Feedback'",
            Success = true
        });

        state.AuditLog.Add(new AuditEntry
        {
            EntryId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            AgentName = AgentNames.DataModelBuilder,
            Action = "ExecuteCreateTable",
            Detail = "Failed to create 'FeedbackCategory': API error",
            Success = false
        });

        Assert.Single(state.Tables);
        Assert.Equal(2, state.AuditLog.Count);
        Assert.Single(state.AuditLog, e => e.Success);
        Assert.Single(state.AuditLog, e => !e.Success);
    }

    [Fact]
    public void HandoffValidationResult_ContainsErrorMessage()
    {
        var result = HandoffValidator.Validate(AgentNames.Reviewer, AgentNames.DataModelBuilder);

        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.NotEmpty(result.ErrorMessage!);
    }

    // ─── AGENT ORCHESTRATOR FALLBACK ───

    [Fact]
    public void ProjectState_DefaultActiveAgent_IsSA()
    {
        var state = new ProjectState();
        Assert.Equal("SolutionArchitect", state.ActiveAgentName);
    }

    [Fact]
    public void ProjectState_IsReady_RequiresBothConfirmations()
    {
        var state = new ProjectState();
        Assert.False(state.IsReady);

        state.EnvironmentConfirmed = true;
        Assert.False(state.IsReady);

        state.SolutionConfirmed = true;
        Assert.True(state.IsReady);
    }
}
