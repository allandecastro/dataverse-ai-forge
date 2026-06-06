using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// planId-by-reference + table/attribute existence validation + duplicate
/// detection for alternate keys. Multi-attribute keys are supported (max 5
/// per Dataverse). The agent passes the planId to execute_create_alternate_key
/// — never the raw payload.
/// </summary>
public class PlanCreateAlternateKeyTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Plan creation of an alternate key on a Dataverse table. Alternate keys enforce "
        + "uniqueness across one or more columns and enable Upsert by natural key. Pass keyAttributesCsv "
        + "with one or MORE attribute logical names separated by commas (e.g., 'eppc_giftcode' OR "
        + "'eppc_giftcode,eppc_recipientemail' for a composite key). Max 5 attributes. Validates that "
        + "the table and every attribute exist, and warns on duplicate keys. Returns a planId — pass it "
        + "verbatim to execute_create_alternate_key (NOT the payload JSON).")]
    public async Task<AlternateKeyCreationPlan> PlanCreateAlternateKeyAsync(
        [Description("Logical name of the table the key lives on (e.g., 'eppc_gift')")] string tableLogicalName,
        [Description("Display name for the alternate key (e.g., 'External Gift ID')")] string keyDisplayName,
        [Description("Publisher customization prefix (e.g., 'eppc')")] string publisherPrefix,
        [Description("Comma-separated list of field LOGICAL names that compose the key (lowercase). "
            + "One name for a single-column key, multiple comma-separated names for a composite key. "
            + "Max 5. Examples: 'eppc_giftcode' or 'eppc_giftcode,eppc_recipientemail'.")] string keyAttributesCsv,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var t = tableLogicalName.ToLowerInvariant();

        var keyAttributes = keyAttributesCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.ToLowerInvariant())
            .ToList();

        // Build the schema/logical name early so even rejected plans carry the
        // intended naming for the agent to reference.
        var sanitized = ToolHelpers.SanitizeSchemaName(keyDisplayName);
        if (string.IsNullOrEmpty(sanitized)) sanitized = "Key";
        var keyLogicalName = $"{publisherPrefix}_{sanitized.ToLowerInvariant()}";
        var keySchemaName = $"{publisherPrefix}_{sanitized}";

        if (keyAttributes.Count == 0)
        {
            return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                "REJECTED: keyAttributesCsv is empty. Pass at least one field logical name "
                + "(e.g. 'eppc_giftcode'). For composite keys pass multiple names separated by commas.");
        }

        if (keyAttributes.Count > 5)
        {
            return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                $"REJECTED: Dataverse alternate keys allow at most 5 attributes; you passed "
                + $"{keyAttributes.Count}. Drop the extras and re-plan.");
        }

        // Validate the table + every attribute exists, AND check for duplicate keys.
        // Per MS Learn (Define alternate keys), only these column types can
        // participate in an alternate key — the index creation will fail at the
        // platform level otherwise.
        var allowedKeyTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "StringType",
            "Integer", "IntegerType",
            "Decimal", "DecimalType",
            "DateTime", "DateTimeType",
            "Lookup", "LookupType",
            "Picklist", "PicklistType",
        };

        try
        {
            // Up-front virtual-table check. Virtual entities (IsLogicalEntity=true)
            // expose data from external sources and don't have a real underlying
            // SQL table, so Dataverse rejects alt-key creation on them. Surface
            // this as a clean rejection rather than waiting for a 400 mid-execute.
            // Per MS Learn: alt keys require a SQL index, and virtual tables don't
            // have one to put the index on.
            try
            {
                var meta = await dataverseClient.GetTableMetadataAsync(t, cancellationToken);
                if (meta.IsLogicalEntity)
                {
                    return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                        $"REJECTED: Table '{t}' is a virtual entity (IsLogicalEntity=true). "
                        + "Alternate keys require a SQL index on the underlying table, which "
                        + "virtual entities don't have. Build the alt key on a non-virtual table.");
                }
            }
            catch (Exception) { /* non-fatal — fall through to GetTableDetails which will surface a clean rejection */ }

            var details = await dataverseClient.GetTableDetailsAsync(t, cancellationToken);
            var columnByName = details.Columns.ToDictionary(
                c => c.LogicalName.ToLowerInvariant(),
                c => c,
                StringComparer.Ordinal);

            var missing = keyAttributes.Where(a => !columnByName.ContainsKey(a)).ToList();
            if (missing.Count > 0)
            {
                var sample = string.Join(", ", columnByName.Keys.OrderBy(s => s).Take(15));
                return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                    $"REJECTED: The following column(s) do NOT exist on table '{t}': "
                    + string.Join(", ", missing) + ". "
                    + "Alternate-key creation will fail at Dataverse with HTTP 400. "
                    + "Available logical names on this table (sample): " + sample + ". "
                    + "Fix the spelling (logical names are lowercase) or create the missing fields first "
                    + "via plan_create_field.");
            }

            // Type validation — Dataverse only allows specific column types in alt keys.
            // Source: https://learn.microsoft.com/en-us/power-apps/developer/data-platform/define-alternate-keys-entity
            var invalidTypes = keyAttributes
                .Where(a => !allowedKeyTypes.Contains(columnByName[a].AttributeType ?? string.Empty))
                .Select(a => $"{a} ({columnByName[a].AttributeType})")
                .ToList();
            if (invalidTypes.Count > 0)
            {
                return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                    $"REJECTED: These columns can NOT be part of an alternate key (Dataverse "
                    + $"restricts alt-key columns to String, Integer, Decimal, DateTime, Lookup, Picklist): "
                    + string.Join(", ", invalidTypes) + ". "
                    + "Per MS Learn: Money, Memo, Boolean, File, Image, BigInt, Double, Customer, Owner "
                    + "are NOT allowed. Pick a different column or change the column type first.");
            }

            // Field-level security check. Per MS Learn: a column protected by
            // field-level security can be used in an alt key but the secured
            // values aren't searchable via the key, which defeats the purpose.
            // Reject so the agent picks a non-secured column.
            var secured = keyAttributes
                .Where(a => columnByName[a].IsSecured)
                .ToList();
            if (secured.Count > 0)
            {
                return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                    $"REJECTED: These columns are field-level-security protected (IsSecured=true): "
                    + string.Join(", ", secured) + ". Secured columns can technically be used in an "
                    + "alt key, but the index won't return secured values to anyone without the "
                    + "field-security profile, defeating the upsert-by-natural-key use case. "
                    + "Pick a non-secured column, or remove the field-security profile first.");
            }

            // Unicode-collation warning for String columns. Per MS Learn:
            // alt-key indexes are case AND accent sensitive, so 'café' ≠ 'cafe'
            // and 'ABC' ≠ 'abc' at lookup time. Most users assume case-insensitive
            // matching from app-tier code; warn proactively so they normalize on
            // write.
            var stringKeys = keyAttributes
                .Where(a => string.Equals(columnByName[a].AttributeType, "StringType", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(columnByName[a].AttributeType, "String", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (stringKeys.Count > 0)
            {
                warnings.Add($"NOTE: String column(s) [{string.Join(", ", stringKeys)}] in this alt key "
                    + "are matched case- AND accent-sensitively at index time ('café' ≠ 'cafe', "
                    + "'ABC' ≠ 'abc'). Normalize values on write (lowercase + strip accents) if you "
                    + "want case-insensitive upsert semantics.");
            }

            // Duplicate detection + max-keys-per-table guard. Dataverse caps
            // alternate keys at 10 per table.
            try
            {
                var existing = await dataverseClient.GetAlternateKeysAsync(t, cancellationToken);
                if (existing.Count >= 10)
                {
                    return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                        $"REJECTED: Table '{t}' already has {existing.Count} alternate keys "
                        + "and Dataverse caps the limit at 10 per table. Delete one of the existing "
                        + "keys via delete_alternate_key before adding a new one.");
                }

                var sortedNew = string.Join(",", keyAttributes.OrderBy(a => a));
                foreach (var k in existing)
                {
                    if (string.Equals(k.LogicalName, keyLogicalName, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"REJECTED: An alternate key named '{k.LogicalName}' already exists on '{t}'. "
                            + "Choose a different display name or delete the existing key first.");
                    }

                    var sortedExisting = string.Join(",",
                        (k.KeyAttributes ?? Array.Empty<string>()).Select(s => s.ToLowerInvariant()).OrderBy(a => a));
                    if (string.Equals(sortedNew, sortedExisting, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"DUPLICATE: An existing key '{k.LogicalName}' already covers the same column "
                            + $"set ({sortedExisting}). Creating a second one is redundant; consider reusing it.");
                    }
                }

                if (warnings.Any(w => w.StartsWith("REJECTED")))
                {
                    return new AlternateKeyCreationPlan(
                        PlanId: $"rejected_alt_key_{Guid.NewGuid():N}",
                        TableLogicalName: t,
                        KeyLogicalName: keyLogicalName,
                        KeyDisplayName: keyDisplayName,
                        KeyAttributes: keyAttributes.AsReadOnly(),
                        WebApiPayloadJson: string.Empty,
                        Warnings: warnings.AsReadOnly(),
                        RiskLevel: PlanRisk.Red);
                }
            }
            catch (Exception) { /* non-fatal — duplicate detection is best-effort */ }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Reject(t, keyLogicalName, keyDisplayName, keyAttributes,
                $"REJECTED: Table '{t}' does not exist in this environment. "
                + "Custom tables typically have a publisher prefix (e.g. 'eppc_gift', not 'gift'). "
                + $"Underlying lookup error: {ex.Message}");
        }

        // Build OData payload. KeyAttributes is a string[] of attribute logical
        // names; LogicalName must be lowercase, SchemaName preserves casing.
        var payload = new Dictionary<string, object>
        {
            ["LogicalName"] = keyLogicalName,
            ["SchemaName"] = keySchemaName,
            ["DisplayName"] = ToolHelpers.BuildLocalizedLabel(keyDisplayName),
            ["KeyAttributes"] = keyAttributes
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"create_alt_key_{Guid.NewGuid():N}";

        // Persist payload server-side. Past failure on other Plan tools: agent
        // re-typed the OData JSON when calling Execute, mangling embedded escaping
        // and producing 400 "Stream was not readable". planId-by-reference fixes
        // it permanently.
        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new AlternateKeyCreationPlan(
            PlanId: planId,
            TableLogicalName: t,
            KeyLogicalName: keyLogicalName,
            KeyDisplayName: keyDisplayName,
            KeyAttributes: keyAttributes.AsReadOnly(),
            WebApiPayloadJson: payloadJson,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: PlanRisk.Yellow);
    }

    private static AlternateKeyCreationPlan Reject(
        string table, string keyLogical, string keyDisplay, IReadOnlyList<string> attrs, string msg) =>
        new(
            PlanId: $"rejected_alt_key_{Guid.NewGuid():N}",
            TableLogicalName: table,
            KeyLogicalName: keyLogical,
            KeyDisplayName: keyDisplay,
            KeyAttributes: attrs,
            WebApiPayloadJson: string.Empty,
            Warnings: new List<string> { msg }.AsReadOnly(),
            RiskLevel: PlanRisk.Red);
}
