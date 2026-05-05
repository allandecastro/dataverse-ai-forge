namespace DataverseForge.Domain.Models;

public record CreatedTable(Guid MetadataId, string LogicalName, string DisplayName, DateTime CreatedAt);

public record CreatedField(Guid MetadataId, string TableLogicalName, string LogicalName, string DisplayName, string FieldType, DateTime CreatedAt);

public record CreatedRelationship(Guid MetadataId, string SchemaName, string PrimaryTable, string RelatedTable, string RelationshipType, DateTime CreatedAt);

public record CreatedOptionSet(Guid MetadataId, string Name, bool IsGlobal, DateTime CreatedAt);

public record CreatedAlternateKey(Guid MetadataId, string TableLogicalName, string KeyLogicalName, string KeyDisplayName, DateTime CreatedAt);

public record CreatedForm(Guid FormId, string TableLogicalName, string Name, string FormType, DateTime CreatedAt);

public record CreatedView(Guid SavedQueryId, string TableLogicalName, string Name, DateTime CreatedAt);

public record CreatedChart(Guid ChartId, string TableLogicalName, string Name, string ChartType, DateTime CreatedAt);

public record CreatedDashboard(Guid DashboardId, string Name, DateTime CreatedAt);

public record CreatedRecord(Guid RecordId, string TableLogicalName, string Description, DateTime CreatedAt);

public record CreatedApp(Guid AppModuleId, string Name, string UniqueName, DateTime CreatedAt);
