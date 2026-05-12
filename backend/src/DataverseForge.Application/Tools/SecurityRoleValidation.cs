using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared parsing/validation helpers for the SecurityBuilder plan tools.
/// Pure functions over agent-supplied tokens — no I/O, no state.
/// </summary>
public static class SecurityRoleValidation
{
    /// <summary>Normalizes an operation token to Dataverse's canonical AccessRight string, or null on unknown.</summary>
    public static string? NormalizeOperation(string? op) =>
        op?.Trim().ToLowerInvariant() switch
        {
            "create" => "Create",
            "read" => "Read",
            "write" or "update" => "Write",
            "delete" => "Delete",
            "append" => "Append",
            "appendto" => "AppendTo",
            "assign" => "Assign",
            "share" => "Share",
            _ => null,
        };

    /// <summary>
    /// Maps a depth token to the wire form (Basic / Local / Deep / Global).
    /// Accepts both maker-portal labels (User / BU / ParentChild / Org) and the wire form.
    /// </summary>
    public static string? NormalizeDepth(string? depth) =>
        depth?.Trim().ToLowerInvariant() switch
        {
            "user" or "basic" => "Basic",
            "bu" or "local" or "businessunit" or "business-unit" => "Local",
            "parentchild" or "parent-child" or "deep" or "buandbelow" => "Deep",
            "org" or "global" or "organization" => "Global",
            _ => null,
        };

    public static bool IsDepthAllowed(PrivilegeMetadata privilege, string depthWire) =>
        depthWire switch
        {
            "Basic" => privilege.CanBeBasic,
            "Local" => privilege.CanBeLocal,
            "Deep" => privilege.CanBeDeep,
            "Global" => privilege.CanBeGlobal,
            _ => false,
        };

    public static string DescribeAllowedDepths(PrivilegeMetadata privilege)
    {
        var allowed = new List<string>();
        if (privilege.CanBeBasic) allowed.Add("User (Basic)");
        if (privilege.CanBeLocal) allowed.Add("BU (Local)");
        if (privilege.CanBeDeep) allowed.Add("ParentChild (Deep)");
        if (privilege.CanBeGlobal) allowed.Add("Org (Global)");
        return allowed.Count == 0 ? "(none)" : string.Join(", ", allowed);
    }
}
