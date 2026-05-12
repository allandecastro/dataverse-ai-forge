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
    /// <summary>DELETE /GlobalOptionSetDefinitions({id}). Removes a global option set
    /// + cascades references on Choice columns. Used by UndoLastOperationTool's
    /// reverse-op for ExecuteCreateGlobalOptionSet (F-25).</summary>
    Task DeleteGlobalOptionSetAsync(Guid metadataId, CancellationToken cancellationToken = default);
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
    /// <summary>
    /// Execute a FetchXML query and return the matching rows as plain JSON
    /// dictionaries. Caller supplies the entity set name (plural) and the
    /// validated <c>&lt;fetch&gt;</c> XML. Caps results at <paramref name="top"/>
    /// (default 50) to keep agent context bounded — agents can also embed
    /// <c>count="N"</c> in the fetch for finer control.
    /// </summary>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> RunFetchXmlAsync(string entitySetName, string fetchXml, int top = 50, CancellationToken cancellationToken = default);
    /// <summary>
    /// Resolve a savedquery's FetchXML and run it. Returns rows in the same
    /// shape as <see cref="RunFetchXmlAsync"/>. Encapsulates the lookups so
    /// the agent only needs the savedQueryId.
    /// </summary>
    Task<Models.SavedQueryRunResult> RunSavedQueryAsync(Guid savedQueryId, int top = 50, CancellationToken cancellationToken = default);
    Task<int> DeleteRecordsBatchAsync(string entitySetName, IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken = default);

    // Dashboards (systemform with type=0 — org-owned only)
    /// <summary>List org-owned dashboards (systemform rows with type=0).</summary>
    Task<IReadOnlyList<Models.DashboardInfo>> GetDashboardsListAsync(CancellationToken cancellationToken = default);
    /// <summary>Read a dashboard's FormXML for inspection / pre-update editing.</summary>
    Task<Models.DashboardDefinition> GetDashboardDefinitionAsync(Guid dashboardId, CancellationToken cancellationToken = default);
    /// <summary>POST /systemforms with type=0. Returns the new dashboard's GUID.
    /// Org-owned dashboards require System Admin / Customizer.</summary>
    Task<Guid> CreateDashboardAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>PATCH /systemforms({id}). Used to update the dashboard's name / description / FormXML.</summary>
    Task UpdateDashboardAsync(Guid dashboardId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>DELETE /systemforms({id}).</summary>
    Task DeleteDashboardAsync(Guid dashboardId, CancellationToken cancellationToken = default);

    // Charts (savedqueryvisualization — org-owned only)
    /// <summary>List charts attached to a table. Used by UI Builder + Tester.</summary>
    Task<IReadOnlyList<Models.ChartInfo>> GetChartsListAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    /// <summary>Read full chart record incl. DataDescription + PresentationDescription
    /// XML blobs. Used by the Tester to verify a chart against a planned spec.</summary>
    Task<Models.ChartDetails> GetChartDetailsAsync(Guid chartId, CancellationToken cancellationToken = default);
    /// <summary>POST /savedqueryvisualizations. Returns the new chart's GUID.
    /// Org-owned charts require System Admin / Customizer.</summary>
    Task<Guid> CreateChartAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>PATCH /savedqueryvisualizations({id}). Updates name / description /
    /// the two XML blobs.</summary>
    Task UpdateChartAsync(Guid chartId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>DELETE /savedqueryvisualizations({id}).</summary>
    Task DeleteChartAsync(Guid chartId, CancellationToken cancellationToken = default);

    // Model-driven apps (appmodule + sitemap + appmoduleroles). Phase 1 is
    // read-only; writes (Create/Update/Delete/Validate/Publish + sitemap CRUD +
    // component add/remove + role assign) land in later phases.
    /// <summary>List model-driven apps in the environment. Filters out unpublished apps when
    /// <paramref name="includeUnpublished"/> is false (default).</summary>
    Task<IReadOnlyList<Models.AppModuleInfo>> GetAppsListAsync(bool includeUnpublished = false, CancellationToken cancellationToken = default);
    /// <summary>Read a single app's metadata (descriptor + configxml included). Returns null
    /// when the app does not exist instead of throwing.</summary>
    Task<Models.AppModuleDetails?> GetAppAsync(Guid appId, CancellationToken cancellationToken = default);
    /// <summary>Composite read: app + components + sitemap + assigned roles in one parallel
    /// fan-out. Returns null when the app does not exist.</summary>
    Task<Models.AppModuleFullDetails?> GetAppFullAsync(Guid appId, CancellationToken cancellationToken = default);
    /// <summary>Read the sitemap currently attached to <paramref name="appId"/> via the
    /// app↔sitemap M:N. Returns null when no sitemap is attached.</summary>
    Task<Models.SiteMapInfo?> GetSiteMapForAppAsync(Guid appId, CancellationToken cancellationToken = default);
    /// <summary>List security roles assigned to the app via the <c>appmoduleroles</c> M:N
    /// association.</summary>
    Task<IReadOnlyList<Models.AppModuleRoleInfo>> ListAppRolesAsync(Guid appId, CancellationToken cancellationToken = default);

    /// <summary>Resolve a webresource (image/script/html/etc.) row by its <c>name</c> column —
    /// returns the GUID, or null when not found. Used by the appmodule create path to embed
    /// the OOB default icon web resource by name (since GUID may differ across environments).</summary>
    Task<Guid?> ResolveWebResourceIdByNameAsync(string webResourceName, CancellationToken cancellationToken = default);

    /// <summary>POST /appmodules with the supplied OData JSON. When
    /// <paramref name="solutionUniqueName"/> is non-empty, sets the
    /// <c>MSCRM.SolutionUniqueName</c> header so Dataverse adds the new app to
    /// that solution atomically (avoids the eventual-consistency retry the
    /// dashboard create path needed). Returns the new appmoduleid.</summary>
    Task<Guid> CreateAppAsync(string webApiPayloadJson, string? solutionUniqueName, CancellationToken cancellationToken = default);
    /// <summary>PATCH /appmodules({id}) with the supplied OData JSON and
    /// <c>If-Match: *</c> header (so Dataverse rejects a stale update if the
    /// row's etag has moved). Used for renaming, description / clienttype /
    /// navigationtype edits.</summary>
    Task UpdateAppAsync(Guid appId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>DELETE /appmodules({id}). Cascades to the app's components in
    /// the appmodulecomponent join table. Managed apps (statecode locked) are
    /// rejected with HTTP 403.</summary>
    Task DeleteAppAsync(Guid appId, CancellationToken cancellationToken = default);
    /// <summary>Calls the bound <c>ValidateApp</c> action on the appmodule.
    /// Returns the validation issue list. When <see cref="Models.AppValidationResult.IsValid"/>
    /// is false, the user can't open the app (typically: no sitemap attached,
    /// or a referenced component was deleted).</summary>
    Task<Models.AppValidationResult> ValidateAppAsync(Guid appId, CancellationToken cancellationToken = default);
    /// <summary>Publishes a single app via the <c>PublishXml</c> action with a
    /// scoped <c>parameterXml</c> envelope. Cheaper than PublishAll when only
    /// the app changed; required after sitemap / component / metadata edits
    /// for the new content to be visible to consumers.</summary>
    Task PublishAppAsync(Guid appId, CancellationToken cancellationToken = default);

    /// <summary>POST /sitemaps with a payload containing <c>sitemapxml</c>,
    /// <c>sitemapname</c>, <c>sitemapnameunique</c>. When
    /// <paramref name="solutionUniqueName"/> is non-empty, sets the
    /// <c>MSCRM.SolutionUniqueName</c> header so the new sitemap lands in
    /// that solution atomically. Returns the new sitemapid.</summary>
    Task<Guid> CreateSiteMapAsync(string webApiPayloadJson, string? solutionUniqueName, CancellationToken cancellationToken = default);
    /// <summary>PATCH /sitemaps({id}) with the supplied OData JSON. Used to
    /// update the sitemap's <c>sitemapxml</c> blob.</summary>
    Task UpdateSiteMapAsync(Guid siteMapId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>DELETE /sitemaps({id}). Required for teardown — the
    /// <c>appmodule</c> cascade only drops the M:N association, not the
    /// sitemap row itself, so a scaffold-created sitemap survives a plain
    /// <c>DeleteApp</c> and blocks re-scaffold with the same uniqueName.</summary>
    Task DeleteSiteMapAsync(Guid siteMapId, CancellationToken cancellationToken = default);
    /// <summary>Attaches components to an app. Returns per-ref outcomes — the
    /// bulk action call is atomic (every non-entity component in the batch
    /// shares one outcome), but each entity attach is independent. Tools use
    /// the per-ref outcomes to report partial success accurately. See audit
    /// finding F-10.</summary>
    Task<IReadOnlyList<Models.AppComponentRefOutcome>> AddAppComponentsAsync(Guid appId, IReadOnlyList<Models.AppComponentRef> components, CancellationToken cancellationToken = default);
    /// <summary>Detaches components from an app (the components themselves are
    /// not deleted). Same parameter and outcome contract as
    /// <see cref="AddAppComponentsAsync"/>.</summary>
    Task<IReadOnlyList<Models.AppComponentRefOutcome>> RemoveAppComponentsAsync(Guid appId, IReadOnlyList<Models.AppComponentRef> components, CancellationToken cancellationToken = default);
    /// <summary>Calls the unbound <c>RetrieveAppComponents</c> function and
    /// flattens the heterogeneous response (entities, forms, savedqueries,
    /// charts, sitemap, …) into a uniform list. Used standalone by the agent
    /// for mid-flow inspection; also called by <see cref="GetAppFullAsync"/>.</summary>
    /// <summary>
    /// Lists all components attached to an app module. Combines two storage
    /// paths because Dataverse uses both:
    ///   - The <c>appmodulecomponent</c> join table (apps we created via the
    ///     Web API deep-insert path).
    ///   - The <c>descriptor</c> JSON column on <c>appmodule</c> (apps the
    ///     maker portal created — entities live in <c>AppComponents.Entities</c>
    ///     with their LogicalName, other components in <c>Components[]</c>).
    /// Caller may pass a pre-fetched <paramref name="descriptorJson"/> to
    /// avoid an extra read; if null we fetch the app to get it.
    /// </summary>
    Task<IReadOnlyList<Models.AppModuleComponentRef>> GetAppComponentsAsync(Guid appId, string? descriptorJson = null, CancellationToken cancellationToken = default);
    /// <summary>Resolve a table by logical name to its MetadataId via
    /// <c>EntityDefinitions(LogicalName='X')?$select=MetadataId</c>. Required
    /// for app components of type "entity": the appmodulecomponent's
    /// <c>objectid</c> column for tables is the entity METADATA id, NOT the
    /// row's primary key. Returns null when the table doesn't exist.</summary>
    Task<Guid?> GetEntityMetadataIdAsync(string tableLogicalName, CancellationToken cancellationToken = default);

    // ── Security roles (Phase 5) ────────────────────────────────────────
    /// <summary>Lists security roles in the environment. Includes BOTH managed
    /// (System Administrator, Sales Person, …) and unmanaged custom roles —
    /// most useful OOB roles are managed and the agent will need to assign
    /// them to apps. Optional filters: name search (case-insensitive contains),
    /// business unit. Used by UIBuilder, SolutionArchitect, and the future
    /// Security Builder agent.</summary>
    Task<IReadOnlyList<Models.SecurityRoleInfo>> GetSecurityRolesAsync(string? searchTerm = null, Guid? businessUnitId = null, CancellationToken cancellationToken = default);
    /// <summary>Single-role read with parent role + customizable flag. Returns
    /// null if the role doesn't exist.</summary>
    Task<Models.SecurityRoleDetails?> GetSecurityRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    /// <summary>Assign a security role to an app via the
    /// <c>appmoduleroles_association</c> M:N navigation. Idempotent — Dataverse
    /// silently accepts re-association of an already-linked role.</summary>
    Task AssignAppRoleAsync(Guid appId, Guid roleId, CancellationToken cancellationToken = default);
    /// <summary>Remove a role assignment from an app. Idempotent — Dataverse
    /// returns 204 even when the role wasn't actually assigned.</summary>
    Task UnassignAppRoleAsync(Guid appId, Guid roleId, CancellationToken cancellationToken = default);

    // ── Role CRUD (Phase 6 — Security Builder agent), full surface ──────
    /// <summary>POST /roles. Returns the new role's GUID (extracted from the
    /// OData-EntityId response header). Privileges are NOT included in this
    /// payload — call <see cref="AddPrivilegesToRoleAsync"/> immediately after
    /// to attach the role's privilege matrix.</summary>
    Task<Guid> CreateRoleAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>PATCH /roles({id}) with <c>If-Match: *</c>. Used for rename /
    /// description-change; privilege changes go through Add/Remove. Refuses
    /// to send an empty payload (no-op patches are a planner bug).</summary>
    Task UpdateRoleAsync(Guid roleId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>DELETE /roles({id}). 404 is treated as idempotent no-op
    /// (matches the F-16 unassign + AppDelete patterns). The caller's
    /// pre-flight check should already have refused on assigned / managed /
    /// system-defined roles, so a 404 here means another writer beat us to
    /// it.</summary>
    Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    /// <summary>POST /roles({id})/Microsoft.Dynamics.CRM.AddPrivilegesRole.
    /// Single call accepts the full add-list. Idempotent on already-attached
    /// privileges (Dataverse upserts).</summary>
    Task AddPrivilegesToRoleAsync(Guid roleId, IReadOnlyList<Models.RolePrivilegeAssignment> privileges, CancellationToken cancellationToken = default);
    /// <summary>POST /roles({id})/Microsoft.Dynamics.CRM.RemovePrivilegeRole.
    /// One privilege per call (Dataverse's API limit) — caller loops in
    /// <c>execute_update_security_role</c>. Errors mid-loop don't roll back
    /// privileges already removed.</summary>
    Task RemovePrivilegeFromRoleAsync(Guid roleId, Guid privilegeId, CancellationToken cancellationToken = default);
    /// <summary>Counts user + team assignments for a role
    /// (<c>systemuserroles</c> + <c>teamroles</c>). Pre-flight safety check
    /// before <c>delete_security_role</c> — non-zero means deletion would
    /// orphan permissions for live principals.</summary>
    Task<int> CountUsersAndTeamsForRoleAsync(Guid roleId, CancellationToken cancellationToken = default);
    /// <summary>GET /roles({id})/roleprivileges_association?$select=privilegeid,privilegedepthmask.
    /// Returns the role's current privilege set with depth. Used by the Update
    /// planner for the privilege delta + by the Tester for verification.</summary>
    Task<IReadOnlyList<Models.RolePrivilegeAssignment>> GetRolePrivilegesAsync(Guid roleId, CancellationToken cancellationToken = default);
    /// <summary>GET /businessunits?$select=businessunitid,name,_parentbusinessunitid_value.
    /// Used by the planner to enumerate BUs when the user didn't specify one.</summary>
    Task<IReadOnlyList<Models.BusinessUnitInfo>> GetBusinessUnitsAsync(CancellationToken cancellationToken = default);
    /// <summary>GET EntityDefinitions(LogicalName='X')?$expand=Privileges.
    /// Used by the planner to resolve "Read on Feedback" → privilege GUID +
    /// allowed depths. Empty list when the table doesn't exist.</summary>
    Task<IReadOnlyList<Models.PrivilegeMetadata>> GetPrivilegesForTableAsync(string tableLogicalName, CancellationToken cancellationToken = default);
    /// <summary>GET /roletemplates?$select=roletemplateid,name,description.
    /// Used by the planner so the agent can pick a template GUID instead of
    /// inventing one. Templates mark a role to auto-replicate into new BUs.</summary>
    Task<IReadOnlyList<Models.RoleTemplateInfo>> GetRoleTemplatesAsync(CancellationToken cancellationToken = default);

    // ── Workflows (Phase 7 — Automation Builder agent) ──────────────────
    // The Dataverse `workflow` entity holds Business Process Flows
    // (category=4), Business Rules (category=2), classic workflows
    // (category=0), actions (category=3), and modern flows (category=5).
    // The Automation Builder agent v1 supports BPF + Business Rule only;
    // the read tool surfaces every category so the agent can answer
    // "what workflows exist?" without restricting visibility.
    /// <summary>GET /workflows filtered by category — BPFs (4), Business
    /// Rules (2), or both when categoryFilter is null. Returns lightweight
    /// metadata only (name, primaryentity, state, customizable flag).</summary>
    Task<IReadOnlyList<Models.WorkflowInfo>> GetWorkflowsAsync(
        Models.WorkflowCategory? categoryFilter = null,
        string? primaryEntityLogicalName = null,
        CancellationToken cancellationToken = default);
    /// <summary>GET /workflows({id}) for a single workflow including the
    /// XAML/clientdata blob. Returns null if not found.</summary>
    Task<Models.WorkflowInfo?> GetWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);
    /// <summary>POST /workflows with the supplied OData JSON payload. The
    /// payload must include name, primaryentity, category, type, mode,
    /// scope, and either clientdata (BR) or xaml (BPF) per the workflow
    /// schema. Returns the new workflow's GUID.</summary>
    Task<Guid> CreateWorkflowAsync(string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>PATCH /workflows({id}) with the supplied OData JSON. Used
    /// for rename + description-change. Activation state changes go
    /// through <see cref="ActivateWorkflowAsync"/> / <see cref="DeactivateWorkflowAsync"/>
    /// because Dataverse rejects PATCH of statecode on workflows.</summary>
    Task UpdateWorkflowAsync(Guid workflowId, string webApiPayloadJson, CancellationToken cancellationToken = default);
    /// <summary>DELETE /workflows({id}). 404 is treated as idempotent
    /// no-op. Managed workflows (IsManaged=true) get a 403 from the
    /// platform — caller's pre-flight should refuse before calling.</summary>
    Task DeleteWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);
    /// <summary>Activates a deactivated workflow via the deprecated-but-
    /// still-supported SetStateRequest (statecode=1, statuscode=2).
    /// Dataverse rejects direct PATCH of statecode on workflows; this
    /// action is the only supported path.</summary>
    Task ActivateWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);
    /// <summary>Deactivates an activated workflow (statecode=0, statuscode=1).
    /// Required before delete + before publishing structural edits to
    /// a BPF.</summary>
    Task DeactivateWorkflowAsync(Guid workflowId, CancellationToken cancellationToken = default);

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
