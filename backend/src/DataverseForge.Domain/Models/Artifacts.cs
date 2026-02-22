namespace DataverseForge.Domain.Models;

public record CreatedTable(Guid MetadataId, string LogicalName, string DisplayName, DateTime CreatedAt);

public record CreatedField(Guid MetadataId, string TableLogicalName, string LogicalName, string DisplayName, string FieldType, DateTime CreatedAt);

public record CreatedRelationship(Guid MetadataId, string SchemaName, string PrimaryTable, string RelatedTable, string RelationshipType, DateTime CreatedAt);

public record CreatedOptionSet(Guid MetadataId, string Name, bool IsGlobal, DateTime CreatedAt);

public record CreatedAlternateKey(Guid MetadataId, string TableLogicalName, string KeyLogicalName, string KeyDisplayName, DateTime CreatedAt);

public record CreatedForm(Guid FormId, string TableLogicalName, string Name, string FormType, DateTime CreatedAt);

public record CreatedView(Guid SavedQueryId, string TableLogicalName, string Name, DateTime CreatedAt);

public record CreatedPlugin(Guid PluginId, string Name, string PrimaryTable, string Message, int Stage, DateTime CreatedAt);

public record CreatedBpf(Guid ProcessId, string Name, string PrimaryTable, DateTime CreatedAt);

public record CreatedBusinessRule(Guid RuleId, string Name, string TableLogicalName, DateTime CreatedAt);

public record CreatedSecurityRole(Guid RoleId, string Name, DateTime CreatedAt);

public record CreatedDashboard(Guid FormId, string Name, DateTime CreatedAt);

public record CreatedModelDrivenApp(Guid AppId, string AppUniqueName, string Name, DateTime CreatedAt);

public record CreatedRecord(Guid RecordId, string TableLogicalName, string Description, DateTime CreatedAt);
