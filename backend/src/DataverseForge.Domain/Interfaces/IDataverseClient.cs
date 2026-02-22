namespace DataverseForge.Domain.Interfaces;

public interface IDataverseClient
{
    Task<Models.EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.SolutionInfo>> GetSolutionsListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.PublisherInfo>> GetPublishersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.TableInfo>> GetTablesListAsync(string filter = "all", string? solutionName = null, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<Models.TableDetails> GetTableDetailsAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Guid> CreateTableAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task AddSolutionComponentAsync(Guid componentId, int componentType, string solutionUniqueName, CancellationToken cancellationToken = default);
    Task<Guid> CreateFieldAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateOneToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateManyToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateGlobalOptionSetAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task InsertOptionValueAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateAlternateKeyAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task ModifyFieldAsync(Guid tableMetadataId, Guid fieldMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.FormInfo>> GetFormsListAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Models.FormDefinition> GetFormDefinitionAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Guid> CreateFormAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.ViewInfo>> GetViewsListAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Guid> CreateViewAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateDashboardAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateModelDrivenAppAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Models.TableMetadata> GetTableMetadataAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Models.FieldMetadata> GetFieldMetadataAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default);
    Task<Models.RelationshipMetadata> GetRelationshipMetadataAsync(string relationshipSchemaName, CancellationToken cancellationToken = default);
    Task<Guid> CreateBpfAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateBusinessRuleAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateCustomApiAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Models.BpfMetadata> GetBpfMetadataAsync(string bpfName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.SecurityRoleInfo>> GetSecurityRolesAsync(string filter = "all", CancellationToken cancellationToken = default);
    Task<Models.SecurityRoleDetails> GetSecurityRoleDetailsAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task<Guid> CreateSecurityRoleAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.FieldSecurityProfileInfo>> GetFieldSecurityProfilesAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreateFieldSecurityProfileAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);

    // Data Population
    Task<Models.BatchOperationResult> CreateRecordsBatchAsync(string entitySetName, IReadOnlyList<string> recordPayloadsJson, CancellationToken cancellationToken = default);
    Task<int> GetRecordCountAsync(string entitySetName, string? filter = null, CancellationToken cancellationToken = default);
    Task<int> DeleteRecordsBatchAsync(string entitySetName, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken = default);

    // Delete operations (for undo support)
    Task DeleteTableAsync(Guid metadataId, CancellationToken cancellationToken = default);
    Task DeleteFieldAsync(Guid tableMetadataId, Guid fieldMetadataId, CancellationToken cancellationToken = default);
    Task DeleteRelationshipAsync(Guid metadataId, CancellationToken cancellationToken = default);
    Task DeleteFormAsync(Guid formId, CancellationToken cancellationToken = default);
    Task DeleteViewAsync(Guid viewId, CancellationToken cancellationToken = default);
    Task DeleteDashboardAsync(Guid dashboardId, CancellationToken cancellationToken = default);
    Task DeleteSecurityRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    Task DeleteWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);
}
