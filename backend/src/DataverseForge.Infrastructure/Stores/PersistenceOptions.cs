namespace DataverseForge.Infrastructure.Stores;

/// <summary>
/// Selects the persistence backend for project state + conversation history.
/// The DB path is convention-based (<c>{writable data dir}/state.db</c>, honouring
/// <c>DVFORGE_DATA_DIR</c>) — see <c>DependencyInjection.AddStores</c>.
/// </summary>
public class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>"InMemory" (default — ephemeral, used by tests) or "Sqlite"
    /// (durable — projects survive a backend restart).</summary>
    public string Provider { get; set; } = "InMemory";
}
