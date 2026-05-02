namespace DataverseForge.Domain.Interfaces;

public interface IDataverseClient
{
    Task<Models.EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.SolutionInfo>> GetSolutionsListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.PublisherInfo>> GetPublishersAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreatePublisherAsync(string uniqueName, string friendlyName, string description, string customizationPrefix, int customizationOptionValuePrefix, CancellationToken cancellationToken = default);
    Task<Guid> CreateSolutionAsync(string uniqueName, string friendlyName, string description, string version, Guid publisherId, CancellationToken cancellationToken = default);
    Task UpdateSolutionVersionAsync(Guid solutionId, string newVersion, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.TableInfo>> GetTablesListAsync(string filter = "all", string? solutionName = null, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<Models.TableDetails> GetTableDetailsAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Guid> CreateTableAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task AddSolutionComponentAsync(Guid componentId, Models.SolutionComponentType componentType, string solutionUniqueName, bool doNotIncludeSubcomponents = false, CancellationToken cancellationToken = default);
    Task<Guid> CreateFieldAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateOneToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateManyToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateGlobalOptionSetAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task InsertOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task InsertStatusValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task UpdateOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task DeleteOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.OptionInfo>> GetLocalOptionSetOptionsAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.OptionInfo>> GetGlobalOptionSetOptionsAsync(string optionSetName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.GlobalOptionSetSummary>> GetGlobalOptionSetsAsync(string? searchTerm = null, bool customOnly = false, CancellationToken cancellationToken = default);
    Task<Guid?> ResolveGlobalOptionSetMetadataIdAsync(string nameOrDisplayName, CancellationToken cancellationToken = default);
    Task<Guid> CreateAlternateKeyAsync(string tableLogicalName, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task ModifyFieldAsync(string tableLogicalName, string fieldLogicalName, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.FormInfo>> GetFormsListAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Models.FormDefinition> GetFormDefinitionAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Guid> CreateFormAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task UpdateFormAsync(Guid formId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.ViewInfo>> GetViewsListAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Guid> CreateViewAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task UpdateViewAsync(Guid viewId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Models.TableMetadata> GetTableMetadataAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Models.FieldMetadata> GetFieldMetadataAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default);
    Task<Models.RelationshipMetadata> GetRelationshipMetadataAsync(string relationshipSchemaName, CancellationToken cancellationToken = default);

    // Data Population
    Task<Models.BatchOperationResult> CreateRecordsBatchAsync(string entitySetName, IReadOnlyList<string> recordPayloadsJson, CancellationToken cancellationToken = default);
    Task<int> GetRecordCountAsync(string entitySetName, string? filter = null, CancellationToken cancellationToken = default);
    Task<int> DeleteRecordsBatchAsync(string entitySetName, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken = default);

    // Delete operations (for undo support)
    Task RemoveSolutionComponentAsync(Guid componentId, Models.SolutionComponentType componentType, string solutionUniqueName, CancellationToken cancellationToken = default);
    Task DeleteSolutionAsync(Guid solutionId, CancellationToken cancellationToken = default);
    Task DeletePublisherAsync(Guid publisherId, CancellationToken cancellationToken = default);
    Task DeleteTableAsync(Guid metadataId, CancellationToken cancellationToken = default);
    Task DeleteFieldAsync(Guid tableMetadataId, Guid fieldMetadataId, CancellationToken cancellationToken = default);
    Task DeleteRelationshipAsync(Guid metadataId, CancellationToken cancellationToken = default);
    Task DeleteFormAsync(Guid formId, CancellationToken cancellationToken = default);
    Task DeleteViewAsync(Guid viewId, CancellationToken cancellationToken = default);
}
