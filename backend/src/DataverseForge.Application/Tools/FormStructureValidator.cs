using System.Text.Json;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Parses + validates the structured tabsJson form layout. Extracted from
/// PlanCreateFormTool; shared with PlanUpdateFormTool.
/// </summary>
internal static class FormStructureValidator
{
    /// <summary>
    /// Splits a comma-separated field list AND lowercases each entry. Dataverse
    /// logical names are lowercase; agents have repeatedly passed PascalCase
    /// like "eppc_GiftName" which Dataverse rejects in formxml's datafieldname.
    /// </summary>
    internal static List<string> NormalizeFieldList(string fields) =>
        string.IsNullOrWhiteSpace(fields)
            ? []
            : fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.ToLowerInvariant())
                .ToList();

    // ───────────────────────────────────────────────────────────────────
    // Structured layout (tabsJson) — phase-1 of "Improve form management"
    // ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse the agent-provided tabsJson into a <see cref="FormStructureSpec"/>.
    /// Case-insensitive property matching so the agent's natural camelCase or
    /// PascalCase input both work. Throws JsonException on malformed input;
    /// caller surfaces that as a REJECTED plan.
    /// Internal so PlanUpdateFormTool can reuse the same parser.
    /// </summary>
    internal static FormStructureSpec ParseStructured(string tabsJson, bool legacyTimeline, string legacyHeaderCsv)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Accept either { tabs: [...], headerFields: [...], includeTimeline: bool }
        // OR a bare array of tabs (the simplest shape) → wrap.
        using var doc = JsonDocument.Parse(tabsJson);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            var bareTabs = JsonSerializer.Deserialize<List<FormTabSpec>>(root.GetRawText(), opts) ?? [];
            return new FormStructureSpec(
                Tabs: bareTabs,
                HeaderFields: NormalizeFieldList(legacyHeaderCsv),
                IncludeTimeline: legacyTimeline);
        }

        var spec = JsonSerializer.Deserialize<FormStructureSpec>(tabsJson, opts)
            ?? throw new JsonException("tabsJson deserialized to null");

        // The legacy `includeTimeline`/`headerFields` parameters still apply when
        // the structured spec is silent on them — preserves the agent's option to
        // mix the two conventions.
        return spec with
        {
            IncludeTimeline = spec.IncludeTimeline || legacyTimeline,
            HeaderFields = spec.HeaderFields is { Count: > 0 } ? spec.HeaderFields : NormalizeFieldList(legacyHeaderCsv),
        };
    }

    /// <summary>
    /// Schema-derived structural validation. Field-level "does this column
    /// exist on the table?" runs separately against the live metadata; this
    /// only catches shape errors that don't need a Dataverse round-trip.
    /// Internal so PlanUpdateFormTool can reuse the same validator.
    /// </summary>
    internal static List<string> ValidateStructured(FormStructureSpec spec)
    {
        var errors = new List<string>();
        if (spec.Tabs is null || spec.Tabs.Count == 0)
        {
            errors.Add("tabs array is empty — at least one tab is required.");
            return errors;
        }
        if (spec.Tabs.Count > 100)
            errors.Add($"tabs count {spec.Tabs.Count} exceeds the FormXML limit of 100.");

        for (var ti = 0; ti < spec.Tabs.Count; ti++)
        {
            var tab = spec.Tabs[ti];
            var tabRef = $"tabs[{ti}]";
            if (string.IsNullOrWhiteSpace(tab.Name)) errors.Add($"{tabRef}.name is required.");
            if (tab.Columns is null || tab.Columns.Count == 0)
            {
                errors.Add($"{tabRef}.columns is empty — at least one column is required.");
                continue;
            }
            if (tab.Columns.Count > 3)
                errors.Add($"{tabRef}.columns count {tab.Columns.Count} exceeds the FormXML limit of 3.");

            // Column widths must each match `^(100|[0-9]{1,2})%$` and sum to 100.
            var totalWidth = 0;
            for (var ci = 0; ci < tab.Columns.Count; ci++)
            {
                var col = tab.Columns[ci];
                var colRef = $"{tabRef}.columns[{ci}]";
                var width = col.Width?.Trim() ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(width, @"^(100|[0-9]{1,2})%$"))
                {
                    errors.Add($"{colRef}.width '{col.Width}' is invalid; must be a percentage like '50%' or '100%'.");
                    continue;
                }
                if (int.TryParse(width.TrimEnd('%'), out var pct)) totalWidth += pct;

                // Validate subgrid params on every cell — phase 2.
                for (var si = 0; si < (col.Sections?.Count ?? 0); si++)
                {
                    var section = col.Sections![si];
                    var sectionRef = $"{colRef}.sections[{si}]";
                    for (var ki = 0; ki < (section.Cells?.Count ?? 0); ki++)
                    {
                        var cell = section.Cells![ki];
                        var cellRef = $"{sectionRef}.cells[{ki}]";
                        ValidateCell(cell, cellRef, errors);
                    }
                }
            }
            if (totalWidth != 100)
                errors.Add($"{tabRef} column widths sum to {totalWidth}%, expected 100%.");
        }

        // ─── Phase 5: form libraries + events ───
        var libNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (spec.Libraries is { Count: > 0 } libs)
        {
            for (var i = 0; i < libs.Count; i++)
            {
                var lib = libs[i];
                var libRef = $"libraries[{i}]";
                if (string.IsNullOrWhiteSpace(lib.Name))
                    errors.Add($"{libRef}.name is required (the JS web resource's logical name).");
                if (string.IsNullOrWhiteSpace(lib.UniqueId) || !Guid.TryParse(lib.UniqueId, out _))
                    errors.Add($"{libRef}.uniqueId '{lib.UniqueId}' must be a valid GUID (the web resource's record ID).");
                if (!string.IsNullOrWhiteSpace(lib.Name)) libNames.Add(lib.Name);
            }
        }

        if (spec.Events is { Count: > 0 } events)
        {
            var validEventTypes = new[] { "DataEvent", "ControlEvent" };
            var validNames = new[] { "onload", "onsave", "onchange" };
            for (var i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                var evRef = $"events[{i}]";
                if (!validEventTypes.Contains(ev.EventType, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{evRef}.eventType '{ev.EventType}' is invalid; must be 'DataEvent' or 'ControlEvent'.");
                if (!validNames.Contains((ev.Name ?? "").ToLowerInvariant()))
                    errors.Add($"{evRef}.name '{ev.Name}' is invalid; must be 'onload', 'onsave', or 'onchange'.");
                if (string.IsNullOrWhiteSpace(ev.FunctionName))
                    errors.Add($"{evRef}.functionName is required.");
                if (string.IsNullOrWhiteSpace(ev.LibraryName))
                    errors.Add($"{evRef}.libraryName is required.");
                else if (libNames.Count > 0 && !libNames.Contains(ev.LibraryName))
                    errors.Add($"{evRef}.libraryName '{ev.LibraryName}' is not in the registered libraries list. Add it to spec.libraries first.");

                // ControlEvent must specify which control. DataEvent must NOT.
                if (string.Equals(ev.EventType, "ControlEvent", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(ev.Attribute) && string.IsNullOrWhiteSpace(ev.Control))
                        errors.Add($"{evRef} is a ControlEvent but neither 'attribute' nor 'control' is set — Dataverse can't bind it.");
                }
            }
        }

        // ─── Phase 6: display conditions + ancestor ───
        if (spec.DisplayConditions is { } dc)
        {
            // Schema choice: Everyone OR Role[] — never both, never neither.
            var hasRoles = dc.RoleIds is { Count: > 0 };
            if (dc.Everyone && hasRoles)
                errors.Add("displayConditions cannot set both Everyone=true and RoleIds — pick one.");
            if (!dc.Everyone && !hasRoles)
                errors.Add("displayConditions requires either Everyone=true or at least one RoleId.");

            if (hasRoles)
            {
                for (var i = 0; i < dc.RoleIds!.Count; i++)
                {
                    if (!Guid.TryParse(dc.RoleIds[i], out _))
                        errors.Add($"displayConditions.roleIds[{i}] '{dc.RoleIds[i]}' is not a valid GUID.");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(spec.AncestorFormId))
        {
            if (!Guid.TryParse(spec.AncestorFormId, out var ancestorGuid))
            {
                errors.Add($"ancestorFormId '{spec.AncestorFormId}' is not a valid GUID.");
            }
            else if (ancestorGuid == Guid.Empty)
            {
                errors.Add("ancestorFormId is the all-zero GUID — Dataverse rejects it with \"Expected non-empty Guid\". Omit ancestorFormId, or pass a real systemform GUID from get_forms_list.");
            }
        }

        // Validate role GUIDs in DisplayConditions — same Guid.Empty bear-trap.
        if (spec.DisplayConditions?.RoleIds is { Count: > 0 } roleIds)
        {
            for (var i = 0; i < roleIds.Count; i++)
            {
                if (!Guid.TryParse(roleIds[i], out var rg))
                {
                    errors.Add($"displayConditions.roleIds[{i}] '{roleIds[i]}' is not a valid GUID.");
                }
                else if (rg == Guid.Empty)
                {
                    errors.Add($"displayConditions.roleIds[{i}] is the all-zero GUID — Dataverse rejects it. Pass real role GUIDs from get_security_roles, or use Everyone=true instead.");
                }
            }
        }

        // Validate library uniqueIds — these are the JS web resource ids referenced
        // by form events. An empty / zero one yields the same Dataverse error.
        if (spec.Libraries is { Count: > 0 } libraries)
        {
            for (var i = 0; i < libraries.Count; i++)
            {
                var libUid = libraries[i].UniqueId;
                if (string.IsNullOrWhiteSpace(libUid))
                {
                    errors.Add($"libraries[{i}].uniqueId is required (the web resource GUID this library refers to).");
                }
                else if (!Guid.TryParse(libUid, out var lg))
                {
                    errors.Add($"libraries[{i}].uniqueId '{libUid}' is not a valid GUID.");
                }
                else if (lg == Guid.Empty)
                {
                    errors.Add($"libraries[{i}].uniqueId is the all-zero GUID — Dataverse rejects it. Pass a real web-resource GUID from the maker portal.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// Per-cell validation. Phase 2: subgrid params (AutoExpand enum, etc.).
    /// Phase 3: control-type rules (Format enum, SizeType enum, required
    /// WebResourceId / QuickForms, valid GUIDs on lookup view fields).
    /// </summary>
    private static void ValidateCell(FormCellSpec cell, string cellRef, List<string> errors)
    {
        var controlType = (cell.ControlType ?? "default").ToLowerInvariant();

        // Validate the control type itself first — silently misspelled types
        // would otherwise fall through to "default" and ship a broken form.
        var supportedTypes = new[] { "default", "owner", "lookup", "subgrid", "webresource", "quickform" };
        if (!supportedTypes.Contains(controlType))
        {
            errors.Add($"{cellRef}.controlType '{cell.ControlType}' is invalid; must be one of: {string.Join(", ", supportedTypes)}.");
            return;
        }

        // ─── Subgrid (phase 2) ─────────────────────────────────────────
        if (controlType == "subgrid")
        {
            if (string.IsNullOrWhiteSpace(cell.TargetEntity))
                errors.Add($"{cellRef}.targetEntity is required for subgrid cells.");

            if (!string.IsNullOrWhiteSpace(cell.AutoExpand))
            {
                var allowed = new[] { "Auto", "Fixed", "AutoWithFixedMax" };
                if (!allowed.Contains(cell.AutoExpand, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.autoExpand '{cell.AutoExpand}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }

            if (cell.RecordsPerPage is int rpp && (rpp < 1 || rpp > 250))
                errors.Add($"{cellRef}.recordsPerPage {rpp} is out of range (1-250).");

            if (cell.MaxRowsBeforeScroll is int mrbs && mrbs < 1)
                errors.Add($"{cellRef}.maxRowsBeforeScroll {mrbs} must be >= 1.");

            if (!string.IsNullOrWhiteSpace(cell.ChartGridMode))
            {
                var allowed = new[] { "ChartOnly", "GridOnly", "ChartAndGrid" };
                if (!allowed.Contains(cell.ChartGridMode, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.chartGridMode '{cell.ChartGridMode}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }

            if (!string.IsNullOrWhiteSpace(cell.VisualizationId))
            {
                if (!Guid.TryParse(cell.VisualizationId, out var visGuid))
                {
                    errors.Add($"{cellRef}.visualizationId '{cell.VisualizationId}' is not a valid GUID.");
                }
                else if (visGuid == Guid.Empty)
                {
                    // Dataverse rejects all-zero Guids with "Expected non-empty Guid"
                    // at create time — surface that as a plan error rather than letting
                    // the HTTP 400 leak back to the user. Past failure (real session):
                    // agent embedded "00000000-0000-0000-0000-000000000000" because it
                    // didn't have a real chart GUID; CreateForm returned HTTP 400.
                    errors.Add($"{cellRef}.visualizationId is the all-zero GUID — Dataverse rejects it with \"Expected non-empty Guid\". Either omit visualizationId / chartGridMode entirely, or call get_charts_list to fetch a real savedqueryvisualizationid for the subgrid's target entity.");
                }
            }

            // Coherence: setting a chart-related param without ChartGridMode is
            // probably a mistake — the chart won't render.
            if ((!string.IsNullOrWhiteSpace(cell.VisualizationId) || cell.EnableChartPicker == true)
                && string.IsNullOrWhiteSpace(cell.ChartGridMode))
            {
                errors.Add($"{cellRef} sets a chart param (visualizationId / enableChartPicker) but chartGridMode is missing — the chart won't render. Set chartGridMode to 'ChartAndGrid' or 'ChartOnly'.");
            }
            return;
        }

        // ─── WebResource (phase 3) ─────────────────────────────────────
        if (controlType == "webresource")
        {
            if (string.IsNullOrWhiteSpace(cell.Params?.WebResourceId))
                errors.Add($"{cellRef}.params.webResourceId is required for webresource cells.");

            if (!string.IsNullOrWhiteSpace(cell.Params?.SizeType))
            {
                var allowed = new[] { "StretchToFit", "StretchMaintainAspectRatio", "Original", "Specific" };
                if (!allowed.Contains(cell.Params!.SizeType, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.params.sizeType '{cell.Params.SizeType}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }
            if (!string.IsNullOrWhiteSpace(cell.Params?.HorizontalAlignment))
            {
                var allowed = new[] { "Left", "Right", "Center", "NotSet" };
                if (!allowed.Contains(cell.Params!.HorizontalAlignment, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.params.horizontalAlignment '{cell.Params.HorizontalAlignment}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }
            if (!string.IsNullOrWhiteSpace(cell.Params?.VerticalAlignment))
            {
                var allowed = new[] { "Top", "Middle", "Bottom", "NotSet" };
                if (!allowed.Contains(cell.Params!.VerticalAlignment, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"{cellRef}.params.verticalAlignment '{cell.Params.VerticalAlignment}' is invalid; must be one of: {string.Join(", ", allowed)}.");
            }
            if (cell.Params?.Height is int h && h < 1)
                errors.Add($"{cellRef}.params.height {h} must be >= 1.");
            if (cell.Params?.Width is int w && w < 1)
                errors.Add($"{cellRef}.params.width {w} must be >= 1.");
            return;
        }

        // ─── QuickForm (phase 3) ───────────────────────────────────────
        if (controlType == "quickform")
        {
            if (string.IsNullOrWhiteSpace(cell.Params?.QuickForms))
                errors.Add($"{cellRef}.params.quickForms is required for quickform cells (one or more form GUIDs, comma- or semicolon-separated).");
            return;
        }

        // ─── default / lookup / owner — typed parameter validation ────
        if (cell.Params is null) return;

        if (!string.IsNullOrWhiteSpace(cell.Params.Format))
        {
            var allowed = new[]
            {
                "SingleLineOfText", "TextArea", "Email", "Url", "Ticker",
                "Date", "DateTime", "DateAndTime",
                "WholeNumber", "DecimalNumber", "Currency"
            };
            if (!allowed.Contains(cell.Params.Format, StringComparer.OrdinalIgnoreCase))
                errors.Add($"{cellRef}.params.format '{cell.Params.Format}' is invalid; must be one of: {string.Join(", ", allowed)}.");
        }

        if (cell.Params.MaxLength is int ml && ml < 1)
            errors.Add($"{cellRef}.params.maxLength {ml} must be >= 1.");
        if (cell.Params.Precision is int p && (p < 0 || p > 10))
            errors.Add($"{cellRef}.params.precision {p} is out of range (0-10).");
        if (cell.Params.MinValue is double minV && cell.Params.MaxValue is double maxV && minV > maxV)
            errors.Add($"{cellRef}.params.minValue {minV} > maxValue {maxV}.");

        if (!string.IsNullOrWhiteSpace(cell.Params.DefaultViewId)
            && !Guid.TryParse(cell.Params.DefaultViewId, out _))
            errors.Add($"{cellRef}.params.defaultViewId '{cell.Params.DefaultViewId}' is not a valid GUID.");
    }
}
