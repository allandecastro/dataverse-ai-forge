namespace DataverseForge.Infrastructure.Stores;

public class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public string Provider { get; set; } = "InMemory";
    public string SqliteConnectionString { get; set; } = "Data Source=dataverse-forge.db";
}
