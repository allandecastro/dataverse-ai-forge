namespace DataverseForge.Domain.Models;

// Structured form layout the agent can request when the simple
// `tab1Fields/tab2Fields/tab3Subgrids` CSV inputs aren't expressive enough.
// Mirrors FormXml.xsd: form → tabs (1..100) → columns (1..3, widths sum to 100)
// → sections → cells. Phase 1 supports default data controls, owner, and
// subgrid cells. Web resources, quick-view embeds, etc. are deferred.

public sealed record FormStructureSpec(
    IReadOnlyList<FormTabSpec> Tabs,
    // Header field logical names (max 4). Optional — header is omitted when null/empty.
    IReadOnlyList<string>? HeaderFields = null,
    bool IncludeTimeline = false,
    // ─── Phase 5: client scripting ───
    // JS web resources the form references. Required for any handler binding.
    IReadOnlyList<FormLibrarySpec>? Libraries = null,
    // OnLoad / OnSave / OnChange / etc. handler bindings.
    IReadOnlyList<FormEventSpec>? Events = null,
    // ─── Phase 6: form selection + inheritance ───
    // Role-based display conditions. When set, only users in one of the listed
    // roles see this form (or everyone, if Everyone=true).
    FormDisplayConditionsSpec? DisplayConditions = null,
    // GUID of the parent form this form derives from. Used by Dataverse to
    // track form lineage; doesn't change layout (the user still controls
    // tabs/sections/cells).
    string? AncestorFormId = null);

/// <summary>
/// Role-based form selection. Either Everyone is true (the form applies to
/// every user — same as omitting display conditions altogether) OR
/// <see cref="RoleIds"/> is non-empty (the form applies only to users with
/// at least one of those security roles).
/// </summary>
public sealed record FormDisplayConditionsSpec(
    bool Everyone = false,
    IReadOnlyList<string>? RoleIds = null,
    // When the user has multiple matching forms, fall back to this one if
    // no other matches. At most one form per table can be the fallback.
    bool? FallbackForm = null,
    // Sort order when multiple forms match the user's roles.
    int? Order = null);

/// <summary>
/// One JS web resource the form depends on. The library's <c>name</c> matches
/// the web resource's logical name (e.g. <c>new_/scripts/giftform.js</c>);
/// <c>uniqueId</c> is the GUID of the web resource record.
/// </summary>
public sealed record FormLibrarySpec(
    string Name,
    string UniqueId);

/// <summary>
/// One event handler binding. <c>EventType</c> is "DataEvent" (form lifecycle:
/// onload/onsave) or "ControlEvent" (per-attribute: onchange). <c>Name</c> is the
/// event name (<c>onload</c>, <c>onsave</c>, <c>onchange</c>). For ControlEvent,
/// set <c>Attribute</c> or <c>Control</c> to specify which control fires the event.
/// </summary>
public sealed record FormEventSpec(
    // "DataEvent" or "ControlEvent". Defaults to DataEvent for OnLoad/OnSave.
    string EventType,
    // Event name: "onload" | "onsave" | "onchange".
    string Name,
    // The JS function name to call (e.g. "Forge.Gift.onLoad").
    string FunctionName,
    // The library logical name (must match a registered FormLibrarySpec.Name).
    string LibraryName,
    // Pass the execution context to the handler. Default true (modern Dataverse).
    bool PassExecutionContext = true,
    // Comma-separated parameters to forward (e.g. "isUpsert").
    string? Parameters = null,
    // Whether the handler is enabled. Default true.
    bool Enabled = true,
    // For ControlEvent: the attribute logical name OR the control id whose
    // event this handler subscribes to. One of the two must be set.
    string? Attribute = null,
    string? Control = null);

public sealed record FormTabSpec(
    string Name,
    // Display label; falls back to Name when null.
    string? Label,
    IReadOnlyList<FormColumnSpec> Columns,
    bool Expanded = true,
    bool Visible = true,
    bool ShowLabel = true,
    bool AvailableForPhone = true,
    // Stack columns vertically on narrow viewports.
    bool VerticalLayout = true);

public sealed record FormColumnSpec(
    // Width string like "100%", "50%", "33%". Sum of all column widths in a
    // tab must equal exactly 100.
    string Width,
    IReadOnlyList<FormSectionSpec> Sections);

public sealed record FormSectionSpec(
    string Name,
    // Display label; falls back to Name when null.
    string? Label,
    IReadOnlyList<FormCellSpec> Cells,
    bool ShowLabel = true,
    bool Visible = true,
    bool AvailableForPhone = true,
    // Section grid columns (1-4). Cells flow left-to-right then wrap. Default 1.
    int Columns = 1);

public sealed record FormCellSpec(
    // Field logical name. Required for "default" / "owner" / "lookup" / "quickform";
    // optional for "subgrid" / "webresource" (those can be unbound).
    string? Field,
    // Control kind. The classid changes for "webresource" and "quickform";
    // the others share the standard data-control classid and pick their renderer
    // from the attribute's metadata + the optional Params block.
    //   - "default": text / number / date / picklist / lookup — common case
    //   - "owner": Owner / Customer
    //   - "lookup": same classid as default, exposes Params for lookup-only options
    //     (DisableMru, FilterRelationshipName, IsInlineNewEnabled, …)
    //   - "subgrid": related-records grid (TargetEntity at top level, phase 2)
    //   - "webresource": HTML/JS web resource embed
    //   - "quickform": embedded quick view form
    string ControlType = "default",
    // Cell vertical span (default 1).
    int Rowspan = 1,
    int Colspan = 1,
    bool ShowLabel = true,
    bool Visible = true,
    bool AvailableForPhone = true,
    // Mark the cell read-only at the form level. Field-level requiredness still wins.
    bool Disabled = false,
    // ─── Phase 3: typed control parameters (TextBox / numeric / picklist /
    //     lookup / web resource / quick form). Only the fields relevant to
    //     the chosen ControlType are emitted; the rest are ignored. ───
    FormCellParams? Params = null,
    // ─── Subgrid-only fields below; ignored for other control types ───
    // The related entity to display (e.g. "eppc_giftline").
    string? TargetEntity = null,
    // The 1:N relationship to filter the grid by. WITHOUT this the subgrid
    // shows ALL rows of the target entity, which is almost never what users
    // want. Required for "show me the lines of THIS gift" subgrids.
    string? RelationshipName = null,
    // Specific savedquery view to use. When omitted Dataverse picks the
    // entity's default view. Use ViewIds (plural) for multi-view picker.
    string? ViewId = null,
    // Comma-separated list of additional savedquery view GUIDs the user can
    // choose from in the picker. Optional.
    string? ViewIds = null,
    bool EnableQuickFind = true,
    bool EnableViewPicker = true,
    // "Auto" | "Fixed" | "AutoWithFixedMax". When "Auto", the grid sizes to its
    // content; "Fixed" caps height; "AutoWithFixedMax" is hybrid.
    string? AutoExpand = null,
    // 1..250. Default Dataverse value is 5.
    int? RecordsPerPage = null,
    // Vertical row cap before the grid scrolls. Independent of RecordsPerPage.
    int? MaxRowsBeforeScroll = null,
    // Show the alphabetical jump-bar above the grid.
    bool? EnableJumpBar = null,
    // "ChartOnly" | "GridOnly" | "ChartAndGrid" — controls whether a chart is
    // rendered alongside / instead of the grid. When set, VisualizationId
    // typically follows.
    string? ChartGridMode = null,
    // Saved-query visualization (chart) GUID to render. Pairs with ChartGridMode.
    string? VisualizationId = null,
    // Show the chart picker so users can switch among the entity's charts.
    bool? EnableChartPicker = null,
    // Show the contextual action bar (Open in new window, Refresh, etc.).
    bool? EnableContextualActions = null);

/// <summary>
/// Per-cell control parameters, keyed by the cell's ControlType. Each control
/// type only consumes the relevant subset of fields — irrelevant ones are
/// ignored at emission time. Mirrors the FormXml.xsd parameter choice groups
/// (TextBox, Numeric, PickList, Lookup, WebResource, QuickForm).
/// </summary>
public sealed record FormCellParams(
    // ─── TextBox (string-typed default cells) ───
    // FormXmlControlType.parameters.Format enum: SingleLineOfText, TextArea,
    // Email, Url, Ticker, Date, DateTime, DateAndTime, WholeNumber,
    // DecimalNumber, Currency. Picks the renderer for string attributes
    // (e.g. Format=TextArea = multi-line input).
    string? Format = null,
    // String length cap. Only meaningful on string attributes.
    int? MaxLength = null,
    // ─── Numeric (whole / decimal / currency) ───
    double? MinValue = null,
    double? MaxValue = null,
    int? Precision = null,
    // ─── PickList ───
    // The OptionValue (integer) to preselect for new records.
    string? DefaultValue = null,
    // ─── Lookup (intersect with default ControlType) ───
    // Specific savedquery view to use as the lookup picker's default.
    string? DefaultViewId = null,
    // Filter the lookup target by an existing 1:N relationship (e.g. only
    // Contacts of THIS Account).
    string? FilterRelationshipName = null,
    // For dependent lookups: only show targets matching another field's value.
    string? DependentAttributeName = null,
    string? DependentAttributeType = null,
    // Hide the "Most recently used" suggestion list.
    bool? DisableMru = null,
    bool? DisableQuickFind = null,
    bool? DisableViewPicker = null,
    // Show the inline "New record" affordance in the picker.
    bool? IsInlineNewEnabled = null,
    // Comma-separated list of additional savedquery view GUIDs the user can
    // pick from in the lookup picker. Optional.
    string? AvailableViewIds = null,
    // ─── WebResource (HTML/JS embed) ───
    // The web resource record to render. Required for webresource cells.
    string? WebResourceId = null,
    // Override URL (rare — usually the WebResourceId is the source of truth).
    string? Url = null,
    // Only relevant when SizeType == "Specific".
    int? Height = null,
    int? Width = null,
    string? AltText = null,
    // FormXmlControlType.parameters.SizeType enum:
    // StretchToFit | StretchMaintainAspectRatio | Original | Specific.
    string? SizeType = null,
    // FormXmlControlType.parameters.HorizontalAlignment enum: Left | Right | Center | NotSet.
    string? HorizontalAlignment = null,
    // FormXmlControlType.parameters.VerticalAlignment enum: Top | Middle | Bottom | NotSet.
    string? VerticalAlignment = null,
    bool? ShowOnMobileClient = null,
    bool? Scrolling = null,
    bool? Border = null,
    // Pass record context to the web resource (record id, table type, etc.).
    bool? PassParameters = null,
    bool? Security = null,
    // ─── QuickForm (embedded quick view form) ───
    // Comma-separated list of quick view form GUIDs to render. Required.
    string? QuickForms = null,
    string? ControlMode = null);
