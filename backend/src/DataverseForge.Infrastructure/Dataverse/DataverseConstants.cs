namespace DataverseForge.Infrastructure.Dataverse;

/// <summary>
/// Centralized Dataverse Web API constants. Used by <see cref="DataverseClient"/>
/// (relative URLs against an HttpClient base) AND <see cref="Settings.SettingsTestService"/>
/// (absolute URLs probed against a user-supplied env URL during Test Connection).
///
/// Single point to bump when Microsoft ships a new API version (v9.3, v10.0).
/// </summary>
internal static class DataverseConstants
{
    /// <summary>
    /// Web API path prefix. Append entity sets, actions, and functions after this.
    /// </summary>
    public const string ApiBase = "api/data/v9.2/";

    /// <summary>
    /// Microsoft-published public client ID for Dataverse interactive auth.
    /// Multi-tenant; <c>http://localhost</c> redirect URI is pre-configured by
    /// Microsoft, so no App Registration setup is needed when this value is used.
    ///
    /// Source: <see href="https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-oauth#simple-example"/>
    /// (the QuickStart sample in the Dataverse Web API docs).
    /// </summary>
    public const string WellKnownPublicClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    /// <summary>
    /// Tenant authority alias for "any work or school account". Used when the
    /// user picked Interactive auth but didn't bother filling in TenantId — the
    /// browser flow lets them sign in with whatever org they belong to, and the
    /// token gets minted for that org's Dataverse env automatically.
    /// </summary>
    public const string OrganizationsAuthority = "organizations";
}
