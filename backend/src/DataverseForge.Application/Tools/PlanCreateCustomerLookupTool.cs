using System.ComponentModel;
using System.Text.Json;
using DataverseForge.Application.Contracts;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Plans a Customer lookup column — a polymorphic lookup hard-locked to
/// {Account, Contact}. Wraps the <c>CreateCustomerRelationships</c> Web API
/// action. Different from PlanCreatePolymorphicLookup because Dataverse uses a
/// dedicated code path for Customer columns: the maker portal shows the field
/// type as "Customer" rather than a generic "Lookup", and the column ID
/// becomes a special <c>customerId</c>-style attribute.
/// </summary>
public class PlanCreateCustomerLookupTool(
    IProjectStateStore projectStateStore,
    ISessionContext sessionContext,
    IDataverseClient dataverseClient)
{
    [Description("Plan a CUSTOMER lookup column — a polymorphic lookup that points to either an Account "
        + "OR a Contact, exposed in Dataverse as the first-class 'Customer' column type. Use this when "
        + "the user asks for 'Customer field' / 'a lookup to either Account or Contact' / 'who is the "
        + "customer'. For polymorphic lookups to OTHER tables, use plan_create_polymorphic_lookup. "
        + "Wraps the Dataverse CreateCustomerRelationships action: atomically creates the Customer column "
        + "and the two 1:N relationships (one to Account, one to Contact). "
        + "Stores the OData JSON server-side under planId.")]
    public async Task<CustomerLookupCreationPlan> PlanCreateCustomerLookupAsync(
        [Description("Logical name of the table that will OWN the Customer column (e.g. 'cr123_invoice')")] string referencingTable,
        [Description("Display name for the Customer column (e.g., 'Customer', 'Bill To', 'Account Owner')")] string lookupDisplayName,
        [Description("Publisher customization prefix used for the column schema name + relationship schema names (e.g. 'cr123')")] string publisherPrefix,
        [Description("Description for the column. Pass empty string for none.")] string description,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var t = (referencingTable ?? string.Empty).Trim().ToLowerInvariant();
        var prefix = (publisherPrefix ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(t))
        {
            return Reject(t, lookupDisplayName, description, "REJECTED: referencingTable is required.");
        }
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return Reject(t, lookupDisplayName, description,
                "REJECTED: publisherPrefix is required (e.g. 'cr123'). Used to build the column "
                + "schema name and the relationship schema names.");
        }
        if (string.IsNullOrWhiteSpace(lookupDisplayName))
        {
            return Reject(t, lookupDisplayName, description, "REJECTED: lookupDisplayName is required.");
        }

        // Verify the referencing table exists.
        try
        {
            _ = await dataverseClient.GetTableMetadataAsync(t, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Reject(t, lookupDisplayName, description,
                $"REJECTED: Referencing table '{t}' does not exist. Underlying error: {ex.Message}");
        }

        var lookupBase = lookupDisplayName.Replace(" ", string.Empty);
        var lookupSchemaName = $"{prefix}_{lookupBase}";

        string Strip(string logical) => logical.StartsWith(prefix + "_", StringComparison.Ordinal)
            ? logical.Substring(prefix.Length + 1)
            : logical;

        // Standard pattern: {prefix}_{strippedTable}_account / _contact.
        var accountRelSchema = $"{prefix}_{Strip(t)}_account";
        var contactRelSchema = $"{prefix}_{Strip(t)}_contact";

        var oneToManyRelationships = new List<Dictionary<string, object>>
        {
            new()
            {
                ["ReferencedEntity"] = "account",
                ["SchemaName"] = accountRelSchema,
                ["ReferencingEntity"] = t,
                ["CascadeConfiguration"] = DefaultCascade(),
            },
            new()
            {
                ["ReferencedEntity"] = "contact",
                ["SchemaName"] = contactRelSchema,
                ["ReferencingEntity"] = t,
                ["CascadeConfiguration"] = DefaultCascade(),
            },
        };

        var lookup = new Dictionary<string, object>
        {
            ["@odata.type"] = "Microsoft.Dynamics.CRM.ComplexLookupAttributeMetadata",
            ["AttributeType"] = "Customer",
            ["AttributeTypeName"] = new Dictionary<string, object> { ["Value"] = "CustomerType" },
            ["SchemaName"] = lookupSchemaName,
            ["DisplayName"] = ToolHelpers.BuildLocalizedLabel(lookupDisplayName),
        };
        if (!string.IsNullOrWhiteSpace(description))
        {
            lookup["Description"] = ToolHelpers.BuildLocalizedLabel(description);
        }

        var payload = new Dictionary<string, object>
        {
            ["OneToManyRelationships"] = oneToManyRelationships,
            ["Lookup"] = lookup,
        };

        var state = await projectStateStore.GetOrCreateAsync(sessionContext.SessionId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(state.SolutionUniqueName))
        {
            payload["SolutionUniqueName"] = state.SolutionUniqueName;
        }
        else
        {
            warnings.Add("No active solution selected — the Customer column will be created in the "
                + "default solution. Call select_solution before execute if you want it in a specific solution.");
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        var planId = $"create_customer_lookup_{Guid.NewGuid():N}";
        state.AddPendingPlan(planId, payloadJson);
        await projectStateStore.SaveAsync(sessionContext.SessionId, state, cancellationToken);

        return new CustomerLookupCreationPlan(
            PlanId: planId,
            ReferencingTable: t,
            LookupSchemaName: lookupSchemaName,
            LookupDisplayName: lookupDisplayName,
            Description: description ?? string.Empty,
            AccountRelationshipSchemaName: accountRelSchema,
            ContactRelationshipSchemaName: contactRelSchema,
            Warnings: warnings.AsReadOnly(),
            RiskLevel: "yellow");
    }

    private static Dictionary<string, object> DefaultCascade() => new()
    {
        ["Assign"] = "NoCascade",
        ["Share"] = "NoCascade",
        ["Delete"] = "RemoveLink",
        ["Merge"] = "NoCascade",
        ["Reparent"] = "NoCascade",
        ["Unshare"] = "NoCascade",
    };

    private static CustomerLookupCreationPlan Reject(
        string referencingTable,
        string displayName,
        string description,
        string warning)
    {
        return new CustomerLookupCreationPlan(
            PlanId: $"rejected_customer_lookup_{Guid.NewGuid():N}",
            ReferencingTable: referencingTable ?? string.Empty,
            LookupSchemaName: string.Empty,
            LookupDisplayName: displayName ?? string.Empty,
            Description: description ?? string.Empty,
            AccountRelationshipSchemaName: string.Empty,
            ContactRelationshipSchemaName: string.Empty,
            Warnings: new List<string> { warning }.AsReadOnly(),
            RiskLevel: "red");
    }
}
