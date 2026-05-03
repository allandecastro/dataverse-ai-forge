using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans a test-data batch. Failure modes the agent kept tripping on:
///   1) Pre-existing tables aren't in <c>state.Tables</c> (only tracks tables
///      created in the current session) — we now fall back to a live
///      <c>GetTableDetailsAsync</c> lookup on every call so the schema is
///      always available.
///   2) Agent calling with <c>recordsJson: "[]"</c> and asking the user "should
///      I refresh metadata?". Empty records is a hard refusal; we return the
///      schema and tell the agent to generate records itself.
///   3) Agent guessing column logical names (e.g. <c>eppc_typeid</c> when the
///      table has <c>eppc_type</c>). We validate field names against the live
///      schema and reject with the specific bad columns + the valid set.
///   4) Lookup-binding format mismatch: Dataverse rejects writes to the lookup
///      column logical name (<c>eppc_contactid</c>) — they must use the
///      navigation property with <c>@odata.bind</c>. We auto-translate so the
///      agent's natural attempt works.
/// We always re-fetch live metadata (vs. caching columns in state.Fields) —
/// the call is cheap relative to the $batch insert and avoids stale schema.
/// </summary>
public class PlanGenerateTestDataTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient,
    ILogger<PlanGenerateTestDataTool> logger)
{
    [Description("Plan generation of realistic test data records for a table. "
        + "Generates varied, locale-aware test records with distributed values. "
        + "Records are tagged with '[TEST DATA]' for later cleanup. "
        + "REQUIRED: recordsJson must be a non-empty JSON array of record objects "
        + "that YOU (the agent) construct using the table's actual column logical "
        + "names. The tool fetches the live schema and rejects unknown columns. "
        + "Lookup fields: just write `<lookup_logical_name>: \"<guid>\"` and the "
        + "tool will rewrite to the correct `@odata.bind` form for you. "
        + "Risk level: yellow (<20 records), orange (>=20 records).")]
    public async Task<TestDataPlan> PlanGenerateTestDataAsync(
        [Description("The logical name of the target table (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("The entity set name for the OData endpoint (e.g., 'eppc_gifts')")] string entitySetName,
        [Description("Number of test records to generate (default: 10)")] int recordCount,
        [Description("REQUIRED non-empty JSON array of record objects you generated, using ONLY "
            + "logical column names that exist on the table. Example: "
            + "[{\"eppc_giftname\":\"Acme Pen\",\"eppc_value\":12.5,\"eppc_contactid\":\"<guid>\"},...]. "
            + "Do NOT pass [], do NOT invent column names. Lookups can be the simple form "
            + "`<lookup>: <guid>` — the tool rewrites them to @odata.bind automatically.")]
        string recordsJson,
        CancellationToken cancellationToken)
    {
        var t = tableLogicalName.ToLowerInvariant();
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        var warnings = new List<string>();

        if (recordCount <= 0) recordCount = 10;

        List<Dictionary<string, object?>> records;
        try
        {
            records = JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(recordsJson) ?? [];
        }
        catch (JsonException)
        {
            records = [];
            warnings.Add("Could not parse records JSON. Provide a valid JSON array — "
                + "e.g. [{\"" + t + "name\":\"Sample 1\"},{\"" + t + "name\":\"Sample 2\"}].");
        }

        // Always fetch live metadata so the schema is in the agent's context on
        // every call (not just the first). Pre-existing tables fail this lookup
        // on a 404; we surface that as a warning rather than throwing.
        TableDetails? liveDetails = null;
        try
        {
            liveDetails = await dataverseClient.GetTableDetailsAsync(t, cancellationToken);
            if (state.Tables.All(tab => tab.LogicalName != t))
            {
                state.Tables.Add(new CreatedTable(
                    Guid.Empty,
                    liveDetails.LogicalName,
                    liveDetails.DisplayName,
                    DateTime.UtcNow));
                await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);
            }
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "PlanGenerateTestData: live metadata lookup failed for {Table}", t);
            warnings.Add($"Live metadata lookup failed for table '{t}': {ex.Message}. "
                + "Confirm the logical name is correct.");
        }

        // Build a lookup map (logical name → nav property + target entity) once,
        // so both the schema hint and the rewrite step use the same source of truth.
        var lookupMap = BuildLookupMap(liveDetails);

        var schemaHint = BuildSchemaHint(t, liveDetails, lookupMap);
        // Always surface the schema. On a successful plan it's informational
        // ("Schema: …"); on a refusal it's actionable (the agent uses it to
        // re-call). Either way the agent keeps the column set in context.
        warnings.Add(schemaHint);

        // Hard refusal: empty records.
        if (records.Count == 0)
        {
            warnings.Insert(0, "REFUSED: recordsJson was empty. You (the agent) must generate the "
                + "record values yourself before calling this tool. Use the schema below to build a "
                + "non-empty JSON array of records, then re-call plan_generate_test_data.");
            return new TestDataPlan(
                TableLogicalName: t,
                EntitySetName: entitySetName,
                RecordCount: 0,
                Records: records.AsReadOnly(),
                WebApiBatchPayloadJson: "{\"entitySetName\":\"" + entitySetName + "\",\"records\":[]}",
                Warnings: warnings.AsReadOnly(),
                RiskLevel: "yellow");
        }

        // Validate field names against the live schema. Catch typos / hallucinated
        // columns BEFORE we waste a $batch round-trip and leak ambiguous Dataverse
        // errors back to the user.
        if (liveDetails is not null)
        {
            var validColumns = new HashSet<string>(
                liveDetails.Columns.Select(c => c.LogicalName),
                StringComparer.OrdinalIgnoreCase);
            // Lookup fields are written as `_logicalname_value` or `logicalname@odata.bind`.
            // Strip the OData-bind suffix and the leading `_` / trailing `_value` so the
            // base logical name is what we compare. Conservative: only flag columns we're
            // *sure* don't exist after normalization.
            var unknownByRecord = new List<string>();
            for (var i = 0; i < records.Count; i++)
            {
                var unknown = records[i].Keys
                    .Where(k => !IsKnownColumn(k, validColumns, lookupMap))
                    .ToList();
                if (unknown.Count > 0)
                {
                    unknownByRecord.Add($"record[{i}]: {string.Join(", ", unknown)}");
                }
            }
            if (unknownByRecord.Count > 0)
            {
                warnings.Insert(0, "REJECTED: records reference columns that do not exist on '"
                    + t + "'. " + string.Join("; ", unknownByRecord)
                    + ". Re-generate using only the logical names listed in the schema below.");
                return new TestDataPlan(
                    TableLogicalName: t,
                    EntitySetName: entitySetName,
                    RecordCount: 0,
                    Records: [],
                    WebApiBatchPayloadJson: "{\"entitySetName\":\"" + entitySetName + "\",\"records\":[]}",
                    Warnings: warnings.AsReadOnly(),
                    RiskLevel: "yellow");
            }
        }

        // Lookup auto-rewrite — runs after column-name validation passes. Walks each
        // record dict and rewrites any key that targets a lookup column to the
        // canonical `<NavProp>@odata.bind: "/<entityset>(<guid>)"` form. If the lookup
        // metadata is incomplete (no nav property name), we reject with a clear warning
        // so we never ship a payload that Dataverse will reject as "undeclared property".
        var rewrittenRecords = new List<Dictionary<string, object?>>(records.Count);
        var rewriteIssues = new List<string>();
        for (var i = 0; i < records.Count; i++)
        {
            var rewritten = RewriteLookupBindings(records[i], lookupMap, i, rewriteIssues);
            rewrittenRecords.Add(rewritten);
        }
        if (rewriteIssues.Count > 0)
        {
            warnings.Insert(0, "REJECTED: " + string.Join("; ", rewriteIssues)
                + ". Refresh table metadata or omit the lookup until it can be resolved.");
            return new TestDataPlan(
                TableLogicalName: t,
                EntitySetName: entitySetName,
                RecordCount: 0,
                Records: [],
                WebApiBatchPayloadJson: "{\"entitySetName\":\"" + entitySetName + "\",\"records\":[]}",
                Warnings: warnings.AsReadOnly(),
                RiskLevel: "yellow");
        }
        records = rewrittenRecords;

        // Best-effort warning for in-session lookup fields without seed data.
        var lookupFields = state.Fields
            .Where(f => f.TableLogicalName == t
                && f.FieldType.Equals("Lookup", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (lookupFields.Count > 0 && state.SeedDataRecords.Count == 0)
        {
            warnings.Add($"Table has {lookupFields.Count} lookup field(s) but no seed data exists. Consider populating reference tables first.");
        }

        // Build record payloads
        var recordPayloads = records
            .Select(r => JsonSerializer.Serialize(r))
            .ToList();

        var batchPayload = JsonSerializer.Serialize(new { entitySetName, records = recordPayloads });

        var riskLevel = recordCount >= 20 ? "orange" : "yellow";
        if (recordCount >= 50)
        {
            warnings.Add($"Generating {recordCount} records. Consider batching in smaller sets for reliability.");
        }

        return new TestDataPlan(
            TableLogicalName: t,
            EntitySetName: entitySetName,
            RecordCount: records.Count,
            Records: records.AsReadOnly(),
            WebApiBatchPayloadJson: batchPayload,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: riskLevel);
    }

    /// <summary>
    /// Lookup column metadata gathered from <c>TableDetails</c>. Indexed by the
    /// LOOKUP column's logical name (case-insensitive) — what the agent naturally
    /// writes — and pointing at the navigation property name the Web API actually
    /// requires plus the OData entity set name to bind to.
    /// </summary>
    private sealed record LookupMeta(
        string ColumnLogicalName,
        string NavigationPropertyName,
        string TargetEntitySetName);

    /// <summary>
    /// Maps every Lookup column on the table to its navigation property + target
    /// entity set. Joins <c>Columns</c> (which has type info) with
    /// <c>Relationships.ReferencingAttribute</c> (which has the nav property) so
    /// only real lookup columns end up in the map.
    /// </summary>
    private static Dictionary<string, LookupMeta> BuildLookupMap(TableDetails? details)
    {
        var map = new Dictionary<string, LookupMeta>(StringComparer.OrdinalIgnoreCase);
        if (details is null) return map;

        var lookupColumnNames = new HashSet<string>(
            details.Columns
                .Where(c => c.AttributeType.Equals("Lookup", StringComparison.OrdinalIgnoreCase)
                    || c.AttributeType.Equals("Owner", StringComparison.OrdinalIgnoreCase)
                    || c.AttributeType.Equals("Customer", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.LogicalName),
            StringComparer.OrdinalIgnoreCase);

        // Only ManyToOne relationships matter for lookups — the referencing
        // attribute on the current table is the lookup column.
        foreach (var r in details.Relationships)
        {
            if (string.IsNullOrEmpty(r.ReferencingAttribute)) continue;
            if (!lookupColumnNames.Contains(r.ReferencingAttribute)) continue;
            if (string.IsNullOrEmpty(r.ReferencingEntityNavigationPropertyName)) continue;

            // First write wins — multiple relationships can point at the same
            // lookup column (rare, but possible); we prefer the first one with
            // a populated nav property.
            if (!map.ContainsKey(r.ReferencingAttribute))
            {
                map[r.ReferencingAttribute] = new LookupMeta(
                    ColumnLogicalName: r.ReferencingAttribute,
                    NavigationPropertyName: r.ReferencingEntityNavigationPropertyName!,
                    TargetEntitySetName: PluralizeEntityName(r.ReferencedEntity));
            }
        }
        return map;
    }

    /// <summary>
    /// Best-effort plural for the OData entity set. Hardcodes the OOB irregulars
    /// that come up in real tenants; defaults to <c>name + "s"</c> for custom
    /// tables (Dataverse's standard convention). TODO: switch to authoritative
    /// EntityDefinitions/EntitySetName lookup if a custom-table irregular plural
    /// (e.g. <c>eppc_quiz → eppc_quizzes</c>) shows up in a trace.
    /// </summary>
    private static string PluralizeEntityName(string entityLogicalName)
    {
        // Pre-pluralized / irregular OOB names. Source: spot-checked against the
        // EntityDefinitions endpoint for a default Dataverse environment.
        return entityLogicalName switch
        {
            "" => "",
            "businessunit" => "businessunits",
            "team" => "teams",
            "systemuser" => "systemusers",
            "activitypointer" => "activitypointers",
            // Default — works for every custom table whose logical name doesn't
            // already end in "s" (which custom tables don't, by convention).
            _ => entityLogicalName.EndsWith('s') ? entityLogicalName : entityLogicalName + "s"
        };
    }

    /// <summary>
    /// Rewrites lookup keys in a single record dict to the canonical
    /// <c>NavProp@odata.bind = "/entityset(guid)"</c> form. Recognises:
    ///   - <c>"eppc_contactid": "&lt;guid&gt;"</c>           → rewrite
    ///   - <c>"eppc_contactid@odata.bind": "/contacts(...)"</c> → rewrite (replace key)
    ///   - <c>"_eppc_contactid_value": "&lt;guid&gt;"</c>    → rewrite
    ///   - already-correct <c>"NavProp@odata.bind"</c>      → pass through
    /// Non-lookup keys pass through untouched.
    /// </summary>
    private static Dictionary<string, object?> RewriteLookupBindings(
        Dictionary<string, object?> record,
        Dictionary<string, LookupMeta> lookupMap,
        int recordIndex,
        List<string> issues)
    {
        var output = new Dictionary<string, object?>(record.Count, StringComparer.Ordinal);
        foreach (var (key, value) in record)
        {
            // Already a nav-property bind — passes through.
            if (key.Contains("@odata.bind", StringComparison.OrdinalIgnoreCase)
                && !TryGetLookupColumnFromBindKey(key, lookupMap, out _))
            {
                output[key] = value;
                continue;
            }

            // Detect lookup column references in any of the three shapes.
            if (TryResolveLookup(key, lookupMap, out var lookup, out var sourceShape))
            {
                if (string.IsNullOrEmpty(lookup!.NavigationPropertyName))
                {
                    issues.Add($"record[{recordIndex}] lookup '{lookup.ColumnLogicalName}' has no navigation property in metadata");
                    continue;
                }
                var guid = ExtractGuid(value, sourceShape);
                if (guid is null)
                {
                    issues.Add($"record[{recordIndex}] lookup '{lookup.ColumnLogicalName}' has no parseable GUID value");
                    continue;
                }
                var rewrittenKey = $"{lookup.NavigationPropertyName}@odata.bind";
                output[rewrittenKey] = $"/{lookup.TargetEntitySetName}({guid})";
                continue;
            }

            output[key] = value;
        }
        return output;
    }

    /// <summary>
    /// Recognises the three shapes the agent has historically used for a lookup
    /// reference and returns the matched <see cref="LookupMeta"/>. Order matters:
    /// the explicit <c>@odata.bind</c> form on a lookup *logical* name has to be
    /// detected before the bare lookup name (which is a substring).
    /// </summary>
    private static bool TryResolveLookup(
        string key,
        Dictionary<string, LookupMeta> lookupMap,
        out LookupMeta? meta,
        out LookupKeyShape shape)
    {
        // Shape 1: bare lookup column logical name (e.g. "eppc_contactid")
        if (lookupMap.TryGetValue(key, out var direct))
        {
            meta = direct;
            shape = LookupKeyShape.Bare;
            return true;
        }

        // Shape 2: "eppc_contactid@odata.bind" (agent used the column logical
        // name with the bind suffix — wrong nav property, but recoverable).
        if (TryGetLookupColumnFromBindKey(key, lookupMap, out direct))
        {
            meta = direct;
            shape = LookupKeyShape.WrongNavBind;
            return true;
        }

        // Shape 3: "_eppc_contactid_value" (the read-side projection — agents
        // see this in GetRecords output and try to write it back).
        if (key.StartsWith('_') && key.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
        {
            var inner = key[1..^"_value".Length];
            if (lookupMap.TryGetValue(inner, out direct))
            {
                meta = direct;
                shape = LookupKeyShape.UnderscoreValue;
                return true;
            }
        }

        meta = null;
        shape = LookupKeyShape.Bare;
        return false;
    }

    private static bool TryGetLookupColumnFromBindKey(
        string key,
        Dictionary<string, LookupMeta> lookupMap,
        out LookupMeta? meta)
    {
        var atIdx = key.IndexOf('@');
        if (atIdx > 0)
        {
            var leftOfAt = key[..atIdx];
            if (lookupMap.TryGetValue(leftOfAt, out var match))
            {
                meta = match;
                return true;
            }
        }
        meta = null;
        return false;
    }

    private enum LookupKeyShape
    {
        Bare,             // "eppc_contactid": "<guid>"
        WrongNavBind,     // "eppc_contactid@odata.bind": "/contacts(<guid>)"
        UnderscoreValue,  // "_eppc_contactid_value": "<guid>"
    }

    /// <summary>
    /// Pulls the GUID out of whatever shape the value is in. For Bare and
    /// UnderscoreValue, the value is the GUID directly (string or boxed Guid).
    /// For WrongNavBind, the value is something like <c>/contacts(&lt;guid&gt;)</c>.
    /// </summary>
    private static string? ExtractGuid(object? value, LookupKeyShape shape)
    {
        if (value is null) return null;
        var s = value switch
        {
            JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
            string str => str,
            Guid g => g.ToString(),
            _ => value.ToString()
        };
        if (string.IsNullOrWhiteSpace(s)) return null;

        if (shape == LookupKeyShape.WrongNavBind)
        {
            // Pull the GUID out of "/entityset(<guid>)".
            var open = s.IndexOf('(');
            var close = s.IndexOf(')');
            if (open >= 0 && close > open)
            {
                s = s[(open + 1)..close];
            }
        }
        return Guid.TryParse(s, out var parsed) ? parsed.ToString() : null;
    }

    /// <summary>
    /// Normalize a record key against the column set. Recognises the various
    /// lookup write shapes plus regular column logical names. We deliberately
    /// accept lookup-bind variants here (even with the wrong nav property) so
    /// the auto-rewrite step downstream can fix them — rejecting at this layer
    /// would surface a confusing "unknown column" error before the agent gets
    /// the chance to see the friendlier rewrite.
    /// </summary>
    private static bool IsKnownColumn(
        string key,
        HashSet<string> validColumns,
        Dictionary<string, LookupMeta> lookupMap)
    {
        if (validColumns.Contains(key)) return true;

        // Anything matching a known lookup shape — bare, @odata.bind, _xxx_value.
        if (lookupMap.ContainsKey(key)) return true;

        var atIdx = key.IndexOf('@');
        if (atIdx > 0)
        {
            var stripped = key[..atIdx];
            if (validColumns.Contains(stripped)) return true;
            if (lookupMap.ContainsKey(stripped)) return true;
        }

        if (key.StartsWith('_') && key.EndsWith("_value", StringComparison.OrdinalIgnoreCase))
        {
            var stripped = key[1..^"_value".Length];
            if (validColumns.Contains(stripped)) return true;
            if (lookupMap.ContainsKey(stripped)) return true;
        }
        return false;
    }

    /// <summary>
    /// Builds a one-shot schema summary the agent can read inside its tool turn.
    /// Format: "Schema for {table} (primary name: x, *=required): logicalName(Type*), …".
    /// Lookup columns include the navigation property + target entity set so the
    /// agent knows where to point them — though the auto-rewrite step also handles
    /// the simple form. We omit secured / system-only columns (createdon, modifiedon,
    /// etc.) so the agent focuses on writable surface.
    /// </summary>
    private static string BuildSchemaHint(
        string tableLogicalName,
        TableDetails? details,
        Dictionary<string, LookupMeta> lookupMap)
    {
        if (details is null) return $"(Schema unavailable — confirm '{tableLogicalName}' is a real table.)";

        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "createdon", "createdby", "createdonbehalfby",
            "modifiedon", "modifiedby", "modifiedonbehalfby",
            "ownerid", "owningbusinessunit", "owninguser", "owningteam",
            "statecode", "statuscode",
            "versionnumber", "importsequencenumber", "overriddencreatedon",
            "timezoneruleversionnumber", "utcconversiontimezonecode"
        };

        var writable = details.Columns
            .Where(c => !hidden.Contains(c.LogicalName))
            .Select(c =>
            {
                var required = c.RequiredLevel.Equals("ApplicationRequired", StringComparison.OrdinalIgnoreCase)
                    || c.RequiredLevel.Equals("SystemRequired", StringComparison.OrdinalIgnoreCase);
                var suffix = required ? "*" : "";
                if (lookupMap.TryGetValue(c.LogicalName, out var lk))
                {
                    return $"{c.LogicalName}(Lookup→{lk.TargetEntitySetName}{suffix})";
                }
                return $"{c.LogicalName}({c.AttributeType}{suffix})";
            })
            .ToList();

        var primary = details.PrimaryNameAttribute;
        return $"Schema for {details.LogicalName} (primary name: {primary}, *=required): " + string.Join(", ", writable) + ".";
    }
}
