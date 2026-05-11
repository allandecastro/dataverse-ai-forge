using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared validation + normalization helpers used by the SecurityBuilder
/// plan tools — <see cref="PlanCreateSecurityRoleTool"/>,
/// <see cref="PlanUpdateSecurityRoleTool"/>, and
/// <see cref="PlanGrantPrivilegeToRolesTool"/>.
///
/// Pure functions over agent-supplied tokens — no I/O, no state. Extracted
/// here so the three plan tools don't carry copy-pasted private static
/// helpers. These are application-layer concerns (parsing the agent's
/// free-form input), not Domain logic, so they stay in
/// <c>DataverseForge.Application.Tools</c> rather than Domain.
/// </summary>
public static class SecurityRoleValidation
{
    /// <summary>
    /// Map an agent-supplied operation token to Dataverse's canonical
    /// AccessRight string. Accepts case-insensitive variants of the eight
    /// operations the privilege model exposes. Returns null on unknowns so
    /// the caller can reject with a clear error.
    /// </summary>
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
    /// Map an agent-supplied depth token to the wire form Dataverse expects
    /// on <c>AddPrivilegesRole</c>. Accepts both the "User / BU / ParentChild
    /// / Org" maker-portal form and the wire form directly ("Basic / Local /
    /// Deep / Global"). Returns null on unknowns.
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

    /// <summary>
    /// Verifies the wire-form depth (Basic / Local / Deep / Global) is
    /// allowed on the privilege, per the four CanBe* flags Dataverse
    /// surfaces on each privilege row. Caller normalizes the depth first.
    /// </summary>
    public static bool IsDepthAllowed(PrivilegeMetadata privilege, string depthWire) =>
        depthWire switch
        {
            "Basic" => privilege.CanBeBasic,
            "Local" => privilege.CanBeLocal,
            "Deep" => privilege.CanBeDeep,
            "Global" => privilege.CanBeGlobal,
            _ => false,
        };

    /// <summary>
    /// Human-readable list of the depths the privilege accepts — used in
    /// the planner's rejection messages so the user sees what's actually
    /// available.
    /// </summary>
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
