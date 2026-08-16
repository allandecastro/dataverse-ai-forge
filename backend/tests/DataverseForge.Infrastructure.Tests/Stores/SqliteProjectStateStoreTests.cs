using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Models;
using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseForge.Infrastructure.Tests.Stores;

public class SqliteProjectStateStoreTests : IDisposable
{
    private readonly SqliteProjectStateStore _store;
    // Use a unique file-based URI with shared cache so the connection persists
    // across multiple open/close cycles (unlike :memory: which is per-connection).
    private readonly string _connectionString;

    public SqliteProjectStateStoreTests()
    {
        var dbName = $"file:test_{Guid.NewGuid():N}?mode=memory&cache=shared";
        _connectionString = $"Data Source={dbName}";
        _store = new SqliteProjectStateStore(_connectionString, NullLogger<SqliteProjectStateStore>.Instance);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetOrCreateAsync_CreatesNewSession()
    {
        var state = await _store.GetOrCreateAsync("session-1");

        Assert.NotNull(state);
        Assert.Equal("session-1", state.ProjectId);
        Assert.NotEqual(default, state.CreatedAt);
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsSameSession()
    {
        var state1 = await _store.GetOrCreateAsync("session-1");
        state1.ProjectName = "Test Project";
        await _store.SaveAsync("session-1", state1);

        var state2 = await _store.GetOrCreateAsync("session-1");

        Assert.Equal("Test Project", state2.ProjectName);
    }

    [Fact]
    public async Task SaveAsync_RoundTrip_PreservesAllFields()
    {
        var state = await _store.GetOrCreateAsync("rt-1");
        state.ProjectName = "Round Trip";
        state.EnvironmentUrl = "https://test.crm.dynamics.com";
        state.SolutionDisplayName = "TestSolution";
        state.ActiveAgentName = "DataModelBuilder";
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "test_table", "Test Table", DateTime.UtcNow));
        state.Fields.Add(new CreatedField(Guid.NewGuid(), "test_table", "test_field", "Test Field", "String", DateTime.UtcNow));

        await _store.SaveAsync("rt-1", state);

        var loaded = await _store.GetOrCreateAsync("rt-1");

        Assert.Equal("Round Trip", loaded.ProjectName);
        Assert.Equal("https://test.crm.dynamics.com", loaded.EnvironmentUrl);
        Assert.Equal("TestSolution", loaded.SolutionDisplayName);
        Assert.Equal("DataModelBuilder", loaded.ActiveAgentName);
        Assert.Single(loaded.Tables);
        Assert.Equal("test_table", loaded.Tables[0].LogicalName);
        Assert.Single(loaded.Fields);
        Assert.Equal("test_field", loaded.Fields[0].LogicalName);
    }

    [Fact]
    public async Task ListSessionsAsync_ReturnsAllSessions()
    {
        var s1 = await _store.GetOrCreateAsync("list-1");
        s1.ProjectName = "Project A";
        s1.SolutionDisplayName = "SolA";
        await _store.SaveAsync("list-1", s1);

        var s2 = await _store.GetOrCreateAsync("list-2");
        s2.ProjectName = "Project B";
        s2.SolutionDisplayName = "SolB";
        await _store.SaveAsync("list-2", s2);

        var sessions = await _store.ListSessionsAsync();

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.SessionId == "list-1" && s.ProjectName == "Project A");
        Assert.Contains(sessions, s => s.SessionId == "list-2" && s.ProjectName == "Project B");
    }

    [Fact]
    public async Task ListSessionsAsync_DenormalizedColumnsMatchState()
    {
        var state = await _store.GetOrCreateAsync("denorm-1");
        state.ProjectName = "Denorm Test";
        state.EnvironmentUrl = "https://env.crm.dynamics.com";
        state.SolutionDisplayName = "DenormSol";
        state.ActiveAgentName = "Tester";
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "t1", "T1", DateTime.UtcNow));
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "t2", "T2", DateTime.UtcNow));
        state.Fields.Add(new CreatedField(Guid.NewGuid(), "t1", "f1", "F1", "String", DateTime.UtcNow));
        await _store.SaveAsync("denorm-1", state);

        var sessions = await _store.ListSessionsAsync();
        var session = sessions.Single(s => s.SessionId == "denorm-1");

        Assert.Equal("Denorm Test", session.ProjectName);
        Assert.Equal("https://env.crm.dynamics.com", session.EnvironmentUrl);
        Assert.Equal("DenormSol", session.SolutionName);
        Assert.Equal("Tester", session.ActiveAgent);
        Assert.Equal(2, session.TableCount);
        Assert.Equal(1, session.FieldCount);
    }

    [Fact]
    public async Task DeleteSessionAsync_RemovesSession()
    {
        await _store.GetOrCreateAsync("del-1");
        await _store.DeleteSessionAsync("del-1");

        var sessions = await _store.ListSessionsAsync();
        Assert.DoesNotContain(sessions, s => s.SessionId == "del-1");
    }

    [Fact]
    public async Task SaveAsync_StackSerialization_RoundTrips()
    {
        var state = await _store.GetOrCreateAsync("stack-1");
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op-1",
            ExecutedAt = DateTime.UtcNow,
            AgentName = "DataModelBuilder",
            ToolName = "execute_create_table",
            IsReversible = true,
            ReverseOp = new ReverseOperation
            {
                ToolName = "delete_table",
                RiskLevel = RiskLevel.High,
                Warning = "Will delete table"
            }
        });
        state.OperationStack.Push(new ExecutedOperation
        {
            OperationId = "op-2",
            ExecutedAt = DateTime.UtcNow,
            AgentName = "DataModelBuilder",
            ToolName = "execute_create_field",
            IsReversible = true
        });
        await _store.SaveAsync("stack-1", state);

        var loaded = await _store.GetOrCreateAsync("stack-1");

        Assert.Equal(2, loaded.OperationStack.Count);
        // JSON serializes Stack<T> top-to-bottom; deserialization reverses order.
        // Verify both operations survived the round-trip.
        var ops = loaded.OperationStack.ToList();
        Assert.Contains(ops, o => o.OperationId == "op-1");
        Assert.Contains(ops, o => o.OperationId == "op-2");
        // Verify ReverseOp nested object survived
        var op1 = ops.Single(o => o.OperationId == "op-1");
        Assert.NotNull(op1.ReverseOp);
        Assert.Equal("delete_table", op1.ReverseOp.ToolName);
        Assert.Equal(RiskLevel.High, op1.ReverseOp.RiskLevel);
    }

    [Fact]
    public async Task SaveAsync_PendingPlans_RoundTrip()
    {
        // PendingPlans was added late in the codebase and feeds into Execute*Tool — a
        // serialization regression here would silently break every Plan→Execute flow.
        var state = await _store.GetOrCreateAsync("plans-1");
        state.PendingPlans["create_table_abc"] = "{\"SchemaName\":\"adc_Test\"}";
        state.PendingPlans["create_field_def"] = "{\"@odata.type\":\"...StringAttributeMetadata\"}";
        await _store.SaveAsync("plans-1", state);

        var loaded = await _store.GetOrCreateAsync("plans-1");

        Assert.Equal(2, loaded.PendingPlans.Count);
        Assert.Equal("{\"SchemaName\":\"adc_Test\"}", loaded.PendingPlans["create_table_abc"]);
        Assert.Contains("StringAttributeMetadata", loaded.PendingPlans["create_field_def"]);
    }

    [Fact]
    public async Task SaveAsync_ExecutedPlans_RoundTrip()
    {
        // ExecutedPlans is what verify_*_against_plan reads — a serialization break
        // would resurface as the false-FAIL cascade we already fought once.
        var state = await _store.GetOrCreateAsync("exec-1");
        state.ExecutedPlans["create_table_xyz"] = "{\"SchemaName\":\"adc_Done\"}";
        await _store.SaveAsync("exec-1", state);

        var loaded = await _store.GetOrCreateAsync("exec-1");

        Assert.Single(loaded.ExecutedPlans);
        Assert.Equal("{\"SchemaName\":\"adc_Done\"}", loaded.ExecutedPlans["create_table_xyz"]);
    }

    [Fact]
    public async Task SaveAsync_FullState_AllRecentFieldsSurvive()
    {
        // One end-to-end round-trip touching every concern (env / solution / publisher /
        // schema / plans / audit / operation stack). Defends against schema-add regressions
        // when fields are added to one of the partial-class files.
        var state = await _store.GetOrCreateAsync("full-1");
        state.EnvironmentName = "MyOrg";
        state.EnvironmentUrl = "https://x.crm.dynamics.com";
        state.EnvironmentConfirmed = true;
        state.SolutionId = Guid.NewGuid();
        state.SolutionUniqueName = "MySol";
        state.SolutionDisplayName = "My Sol";
        state.SolutionConfirmed = true;
        state.PublisherId = Guid.NewGuid();
        state.PublisherPrefix = "abc";
        state.PublisherOptionValuePrefix = 12345;
        state.Tables.Add(new CreatedTable(Guid.NewGuid(), "abc_t", "T", DateTime.UtcNow));
        state.Fields.Add(new CreatedField(Guid.NewGuid(), "abc_t", "abc_f", "F", "String", DateTime.UtcNow));
        state.PendingPlans["p1"] = "{\"a\":1}";
        state.ExecutedPlans["e1"] = "{\"b\":2}";
        state.AuditLog.Add(new AuditEntry { EntryId = "a", Timestamp = DateTime.UtcNow, AgentName = "SA", Action = "x", Detail = "y", Success = true });
        state.OperationStack.Push(new ExecutedOperation { OperationId = "o1", ExecutedAt = DateTime.UtcNow, AgentName = "SA", ToolName = "ExecuteCreateTable", IsReversible = false });

        await _store.SaveAsync("full-1", state);
        var loaded = await _store.GetOrCreateAsync("full-1");

        Assert.Equal("MyOrg", loaded.EnvironmentName);
        Assert.True(loaded.EnvironmentConfirmed);
        Assert.Equal(state.SolutionId, loaded.SolutionId);
        Assert.True(loaded.SolutionConfirmed);
        Assert.Equal("abc", loaded.PublisherPrefix);
        Assert.Equal(12345, loaded.PublisherOptionValuePrefix);
        Assert.Single(loaded.Tables);
        Assert.Single(loaded.Fields);
        Assert.Equal("{\"a\":1}", loaded.PendingPlans["p1"]);
        Assert.Equal("{\"b\":2}", loaded.ExecutedPlans["e1"]);
        Assert.Single(loaded.AuditLog);
        Assert.Single(loaded.OperationStack);
    }

    [Fact]
    public async Task SaveAsync_AuditLog_RoundTrips()
    {
        var state = await _store.GetOrCreateAsync("audit-1");
        state.AuditLog.Add(new AuditEntry
        {
            EntryId = "a-1",
            Timestamp = DateTime.UtcNow,
            AgentName = "SolutionArchitect",
            Action = "GetEnvironmentInfo",
            Detail = "Retrieved environment info",
            Success = true
        });
        await _store.SaveAsync("audit-1", state);

        var loaded = await _store.GetOrCreateAsync("audit-1");

        Assert.Single(loaded.AuditLog);
        Assert.Equal("a-1", loaded.AuditLog[0].EntryId);
        Assert.Equal("GetEnvironmentInfo", loaded.AuditLog[0].Action);
        Assert.True(loaded.AuditLog[0].Success);
    }

    [Fact]
    public async Task GetOrCreateAsync_EmptyState_HasDefaults()
    {
        var state = await _store.GetOrCreateAsync("empty-1");

        Assert.Empty(state.Tables);
        Assert.Empty(state.Fields);
        Assert.Empty(state.Relationships);
        Assert.Empty(state.AuditLog);
        Assert.Empty(state.OperationStack);
        Assert.Equal("SolutionArchitect", state.ActiveAgentName);
    }

    [Fact]
    public async Task ApplyAndSaveAsync_ConcurrentWrites_NoLostUpdates()
    {
        // F-23: without the per-session lock in ApplyAndSaveAsync, concurrent
        // read-mutate-save cycles on the SQLite store (each GetOrCreate returns a
        // FRESH deserialized copy) would clobber each other and lose writes.
        const int writers = 25;
        await _store.GetOrCreateAsync("concurrent-1");

        var tasks = Enumerable.Range(0, writers).Select(i =>
            _store.ApplyAndSaveAsync("concurrent-1", s =>
                s.Tables.Add(new CreatedTable(Guid.NewGuid(), $"t_{i}", $"Table {i}", DateTime.UtcNow))));
        await Task.WhenAll(tasks);

        var loaded = await _store.GetOrCreateAsync("concurrent-1");
        Assert.Equal(writers, loaded.Tables.Count);
    }
}
