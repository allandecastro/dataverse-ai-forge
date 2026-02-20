using System.ComponentModel;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class CheckOobTableCoverageTool
{
    private static readonly List<OobTableEntry> OobCatalog =
    [
        new("account", ["company", "organization", "vendor", "firm", "business", "corporation", "employer"],
            ["name", "address", "phone", "email", "website", "industry", "revenue", "numberofemployees"]),
        new("contact", ["person", "individual", "customer", "member", "employee", "user", "participant"],
            ["firstname", "lastname", "email", "phone", "address", "jobtitle", "birthdate"]),
        new("incident", ["case", "ticket", "issue", "support request", "service request", "complaint", "problem"],
            ["title", "description", "priority", "status", "customer", "subject", "origin"]),
        new("lead", ["prospect", "potential customer", "sales lead", "inquiry"],
            ["firstname", "lastname", "email", "company", "jobtitle", "phone", "subject"]),
        new("opportunity", ["deal", "sales opportunity", "potential deal", "bid", "proposal"],
            ["name", "amount", "closedate", "stage", "probability", "customer"]),
        new("task", ["todo", "to-do", "action item", "activity", "assignment", "work item"],
            ["subject", "description", "duedate", "priority", "status", "owner"]),
        new("email", ["message", "mail", "correspondence", "email message"],
            ["subject", "body", "from", "to", "cc", "regardingobject"]),
        new("phonecall", ["phone call", "call", "telephone call", "phone activity"],
            ["subject", "phonenumber", "direction", "duration", "description"]),
        new("annotation", ["note", "comment", "remark", "attachment", "memo"],
            ["subject", "notetext", "filename", "mimetype", "objectid"]),
        new("queue", ["queue", "work queue", "service queue", "routing queue"],
            ["name", "description", "email", "queuetype"]),
        new("product", ["product", "item", "goods", "merchandise", "sku", "catalog item"],
            ["name", "productnumber", "description", "price", "quantity", "producttype"]),
        new("appointment", ["meeting", "appointment", "booking", "calendar event", "schedule"],
            ["subject", "starttime", "endtime", "location", "description", "organizer"]),
        new("campaign", ["campaign", "marketing campaign", "promotion", "marketing initiative"],
            ["name", "codename", "type", "status", "startdate", "enddate", "budget"]),
        new("invoice", ["invoice", "bill", "billing", "payment request"],
            ["name", "invoicenumber", "totalamount", "duedate", "customer", "status"]),
        new("salesorder", ["order", "sales order", "purchase order", "order form"],
            ["name", "ordernumber", "totalamount", "customer", "status", "requestdate"]),
        new("quote", ["quote", "quotation", "estimate", "price quote", "bid"],
            ["name", "quotenumber", "totalamount", "customer", "validuntil", "status"]),
        new("knowledgearticle", ["article", "knowledge base", "kb article", "faq", "documentation", "help article"],
            ["title", "content", "keywords", "description", "status", "publishdate"]),
        new("systemuser", ["user", "system user", "agent", "operator", "staff"],
            ["fullname", "email", "businessunit", "title", "department"]),
        new("team", ["team", "group", "department", "unit", "workgroup"],
            ["name", "description", "businessunit", "administrator"]),
        new("businessunit", ["business unit", "division", "department", "branch", "office"],
            ["name", "parentbusinessunit", "address", "website"])
    ];

    [Description("Check if a proposed custom table overlaps with existing Dataverse out-of-the-box (OOB) tables. Returns recommendations for OOB tables that might already cover the intended use case, sorted by match confidence.")]
    public Task<IReadOnlyList<OobTableRecommendation>> CheckOobTableCoverageAsync(
        [Description("The proposed table name")] string proposedTableName,
        [Description("Description of what the table will store")] string proposedDescription,
        [Description("List of intended field names for the proposed table")] string[] intendedFields,
        CancellationToken cancellationToken)
    {
        var proposedNameLower = proposedTableName.ToLowerInvariant();
        var proposedDescLower = proposedDescription.ToLowerInvariant();
        var intendedFieldsLower = intendedFields.Select(f => f.ToLowerInvariant()).ToHashSet();

        var recommendations = new List<OobTableRecommendation>();

        foreach (var entry in OobCatalog)
        {
            var confidence = 0;
            var reasons = new List<string>();

            // Check exact logical name match
            if (proposedNameLower == entry.LogicalName)
            {
                confidence += 50;
                reasons.Add($"Exact name match with OOB table '{entry.LogicalName}'");
            }

            // Check alias matches against proposed name
            foreach (var alias in entry.Aliases)
            {
                if (proposedNameLower.Contains(alias) || alias.Contains(proposedNameLower))
                {
                    confidence += 35;
                    reasons.Add($"Name matches alias '{alias}'");
                    break; // Only count name alias once
                }
            }

            // Check alias matches against proposed description
            foreach (var alias in entry.Aliases)
            {
                if (proposedDescLower.Contains(alias))
                {
                    confidence += 15;
                    reasons.Add($"Description mentions '{alias}'");
                    break; // Only count description alias once
                }
            }

            // Check logical name in description
            if (proposedDescLower.Contains(entry.LogicalName))
            {
                confidence += 15;
                reasons.Add($"Description mentions '{entry.LogicalName}'");
            }

            // Check field overlap
            var matchingFields = entry.KeyFields.Where(f => intendedFieldsLower.Any(pf => pf.Contains(f) || f.Contains(pf))).ToList();
            if (matchingFields.Count > 0)
            {
                var fieldConfidence = (int)(((double)matchingFields.Count / entry.KeyFields.Count) * 30);
                confidence += fieldConfidence;
                reasons.Add($"{matchingFields.Count}/{entry.KeyFields.Count} fields overlap: {string.Join(", ", matchingFields)}");
            }

            // Cap at 100
            confidence = Math.Min(confidence, 100);

            if (confidence > 20)
            {
                var missingFields = intendedFieldsLower
                    .Where(f => !entry.KeyFields.Any(kf => f.Contains(kf) || kf.Contains(f)))
                    .ToList();

                recommendations.Add(new OobTableRecommendation(
                    OobTableName: entry.LogicalName,
                    MatchConfidence: confidence,
                    Explanation: string.Join("; ", reasons),
                    MissingFields: missingFields.AsReadOnly(),
                    CoverageSummary: $"OOB table '{entry.LogicalName}' covers {matchingFields.Count}/{intendedFields.Length} of your intended fields."));
            }
        }

        var result = recommendations
            .OrderByDescending(r => r.MatchConfidence)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<OobTableRecommendation>>(result);
    }

    private sealed record OobTableEntry(string LogicalName, List<string> Aliases, List<string> KeyFields);
}
