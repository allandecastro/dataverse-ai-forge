namespace DataverseForge.Infrastructure.Dataverse;

/// <summary>
/// How the backend authenticates against Dataverse.
/// </summary>
public enum DataverseAuthMode
{
    /// <summary>
    /// App Registration with a client secret (TenantId + ClientId + ClientSecret).
    /// Default — works headless, suitable for shared / server / CI use.
    /// </summary>
    ServicePrincipal,

    /// <summary>
    /// Interactive browser sign-in with the running user's account
    /// (TenantId + ClientId; NO secret). Opens a browser tab on the BACKEND's
    /// machine — fine for localhost dev, won't work for a deployed backend
    /// where no human is at the server. Requires the App Registration to be
    /// configured as a public client with <c>http://localhost</c> as a
    /// redirect URI.
    /// </summary>
    Interactive,
}

public sealed class DataverseOptions
{
    public const string SectionName = "Dataverse";

    public string EnvironmentUrl { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Authentication mode. Defaults to <see cref="DataverseAuthMode.ServicePrincipal"/>
    /// for back-compat with existing user-settings.json files.
    /// </summary>
    public DataverseAuthMode AuthMode { get; set; } = DataverseAuthMode.ServicePrincipal;

    /// <summary>
    /// When true, every Dataverse HTTP call's request + response body is logged
    /// at Information level by <see cref="DataverseLoggingHandler"/>. Use this
    /// to see exactly what we sent and got back during local debugging — you'll
    /// see lines like "Dataverse → POST /api/data/v9.2/EntityDefinitions" with
    /// the full JSON payload in the next line.
    ///
    /// Off by default (silent in production / shared logs). Bodies can contain
    /// schema details, environment names, etc. — fine for solo dev, careful in
    /// shared deployments.
    /// </summary>
    public bool VerboseLogging { get; set; }
}
