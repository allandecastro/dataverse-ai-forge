namespace DataverseForge.Domain.Interfaces;

public interface IDataverseClient
{
    Task<Models.EnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.SolutionInfo>> GetSolutionsListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.PublisherInfo>> GetPublishersAsync(CancellationToken cancellationToken = default);
    Task<Guid> CreatePublisherAsync(string uniqueName, string friendlyName, string description, string customizationPrefix, int customizationOptionValuePrefix, CancellationToken cancellationToken = default);
    Task<Guid> CreateSolutionAsync(string uniqueName, string friendlyName, string description, string version, Guid publisherId, CancellationToken cancellationToken = default);
    Task UpdateSolutionVersionAsync(Guid solutionId, string newVersion, CancellationToken cancellationToken = default);
    /// <summary>
    /// Exports a Dataverse solution as a .zip via the <c>ExportSolution</c>
    /// action. Returns the raw bytes of the package; caller decides where to
    /// persist them (workspace folder, temp file, download endpoint, …).
    /// </summary>
    Task<byte[]> ExportSolutionAsync(string solutionUniqueName, bool managed, CancellationToken cancellationToken = default);
    /// <summary>
    /// Publishes ALL pending customizations across the environment via the
    /// <c>PublishAllXml</c> Web API action. Required after schema/UI changes
    /// so they're visible to consumers (model-driven apps, exported solution
    /// zips, downstream Power Automate flows).
    /// </summary>
    Task PublishAllAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// Reads the organization's capacity utilization (active users, custom
    /// entities, storage, workflows) via the <c>RetrieveOrganizationResources</c>
    /// function. Read-only — useful for "are we close to a cap?" answers.
    /// </summary>
    Task<Models.OrganizationResources> RetrieveOrganizationResourcesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.TableInfo>> GetTablesListAsync(string filter = "all", string? solutionName = null, string? searchTerm = null, CancellationToken cancellationToken = default);
    Task<Models.TableDetails> GetTableDetailsAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Guid> CreateTableAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task ModifyTableAsync(string tableLogicalName, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task AddSolutionComponentAsync(Guid componentId, Models.SolutionComponentType componentType, string solutionUniqueName, bool doNotIncludeSubcomponents = false, CancellationToken cancellationToken = default);
    Task<Guid> CreateFieldAsync(Guid tableMetadataId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateOneToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Guid> CreateManyToManyRelationshipAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>
    /// Checks whether a table can participate in a many-to-many relationship via
    /// the <c>CanManyToMany</c> Web API action. Some OOB tables and all virtual
    /// entities reject N:N — this action surfaces the constraint at plan time.
    /// </summary>
    Task<bool> CanManyToManyAsync(string entityName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates a multi-table polymorphic lookup column via the
    /// <c>CreatePolymorphicLookupAttribute</c> action. Returns the AttributeId of the
    /// new lookup column (NOT the relationship metadata IDs — those are separate
    /// objects and must be queried after the fact if needed).
    /// </summary>
    Task<Guid> CreatePolymorphicLookupAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>
    /// Creates a Customer lookup (polymorphic Account+Contact) via the
    /// <c>CreateCustomerRelationships</c> action. Returns the AttributeId.
    /// </summary>
    Task<Guid> CreateCustomerLookupAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
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
    Task DeleteAlternateKeyAsync(string tableLogicalName, Guid keyMetadataId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Deactivates an alternate key without deleting it via the
    /// <c>DeactivateEntityKey</c> action. The key, its definition, and its
    /// underlying SQL index stay in place but the uniqueness constraint stops
    /// being enforced and Upsert-by-key calls fail. Reverse with the maker
    /// portal's "Activate" button (we don't expose an activate path yet).
    /// </summary>
    Task DeactivateAlternateKeyAsync(string tableLogicalName, string keyLogicalName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Reactivates a previously-deactivated alternate key via the
    /// <c>ReactivateEntityKey</c> action. The platform queues an async system
    /// job to rebuild the SQL index — the call returns immediately even though
    /// the index isn't usable yet.
    /// </summary>
    Task ReactivateAlternateKeyAsync(string tableLogicalName, string keyLogicalName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.AlternateKeyInfo>> GetAlternateKeysAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task ModifyFieldAsync(string tableLogicalName, string fieldLogicalName, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.FormInfo>> GetFormsListAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Models.FormDefinition> GetFormDefinitionAsync(Guid formId, CancellationToken cancellationToken = default);
    Task<Guid> CreateFormAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task UpdateFormAsync(Guid formId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>
    /// Clones an existing systemform via the bound <c>CopySystemForm</c> action.
    /// The Target body lets the caller override <c>name</c>/<c>description</c>
    /// on the new form; everything else (FormXML, type, objecttypecode, etc.)
    /// is inherited from the source. Returns the new form's id.
    /// </summary>
    Task<Guid> CopyFormAsync(Guid sourceFormId, string newFormName, string newDescription, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Models.ViewInfo>> GetViewsListAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Guid> CreateViewAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task UpdateViewAsync(Guid viewId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    Task<Models.TableMetadata> GetTableMetadataAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    Task<Models.FieldMetadata> GetFieldMetadataAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default);
    Task<Models.RelationshipMetadata> GetRelationshipMetadataAsync(string relationshipSchemaName, CancellationToken cancellationToken = default);

    // Data Population
    Task<Models.BatchOperationResult> CreateRecordsBatchAsync(string entitySetName, IReadOnlyList<string> recordPayloadsJson, CancellationToken cancellationToken = default);
    Task<int> GetRecordCountAsync(string entitySetName, string? filter = null, CancellationToken cancellationToken = default);
    /// <summary>
    /// Read up to <paramref name="top"/> rows from <paramref name="entitySetName"/>
    /// as plain JSON dictionaries. Read-only — used by DataPopulation to fetch real
    /// GUIDs for lookup targets, and by the Tester for sample-row verification.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> GetRecordsAsync(string entitySetName, string? select = null, string? filter = null, string? orderby = null, int top = 5, CancellationToken cancellationToken = default);
    Task<int> DeleteRecordsBatchAsync(string entitySetName, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken = default);

    // Delete operations (for undo support)
    Task RemoveSolutionComponentAsync(Guid componentId, Models.SolutionComponentType componentType, string solutionUniqueName, CancellationToken cancellationToken = default);
    Task DeleteSolutionAsync(Guid solutionId, CancellationToken cancellationToken = default);
    Task DeletePublisherAsync(Guid publisherId, CancellationToken cancellationToken = default);
    Task DeleteTableAsync(Guid metadataId, CancellationToken cancellationToken = default);
    Task DeleteFieldAsync(Guid tableMetadataId, Guid fieldMetadataId, CancellationToken cancellationToken = default);
    Task DeleteFieldByLogicalNameAsync(string tableLogicalName, string fieldLogicalName, CancellationToken cancellationToken = default);
    Task DeleteRelationshipAsync(Guid metadataId, CancellationToken cancellationToken = default);
    Task DeleteFormAsync(Guid formId, CancellationToken cancellationToken = default);
    Task DeleteViewAsync(Guid viewId, CancellationToken cancellationToken = default);
}
