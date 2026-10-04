namespace DataverseForge.Application.Contracts;

/// <summary>
/// Per-project credentials. Each project (session) targets its own Dataverse environment
/// and, optionally, its own Azure DevOps backlog — a client's credentials must never be
/// used for another client's project. Azure AI stays global (it is the app's LLM account).
///
/// Kept OUT of <c>ProjectState</c> on purpose: that state is persisted as plaintext JSON
/// and streamed to the UI. Implementations encrypt the secrets at rest.
/// </summary>
public interface IProjectConnectionStore
{
    /// <summary>The project's connection, or null when none has been configured yet.</summary>
    Task<ProjectConnection?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task SaveAsync(string sessionId, ProjectConnection connection, CancellationToken cancellationToken = default);

    Task DeleteAsync(string sessionId, CancellationToken cancellationToken = default);
}

/// <summary>All the external connections of one project.</summary>
public sealed record ProjectConnection(DataverseConnection? Dataverse, AzureDevOpsConnection? AzureDevOps)
{
    public static readonly ProjectConnection Empty = new(null, null);
}

/// <summary>
/// A project's Dataverse connection. <see cref="EnvironmentUrl"/> is fixed once set: the
/// project's plans and history were built against that environment.
/// </summary>
/// <param name="AuthMode">"ServicePrincipal" or "Interactive".</param>
public sealed record DataverseConnection(
    string EnvironmentUrl,
    string AuthMode,
    string TenantId,
    string ClientId,
    string ClientSecret)
{
    public bool IsInteractive => string.Equals(AuthMode, "Interactive", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(EnvironmentUrl)
        && (IsInteractive
            || (!string.IsNullOrWhiteSpace(TenantId)
                && !string.IsNullOrWhiteSpace(ClientId)
                && !string.IsNullOrWhiteSpace(ClientSecret)));
}

public sealed record AzureDevOpsConnection(string OrganizationUrl, string Project, string PersonalAccessToken)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(OrganizationUrl)
        && !string.IsNullOrWhiteSpace(Project)
        && !string.IsNullOrWhiteSpace(PersonalAccessToken);
}

/// <summary>
/// Thrown when a Dataverse call is made for a project that has no usable connection.
/// The message is written for the agent to relay to the user as-is.
/// </summary>
public sealed class ProjectConnectionMissingException(string message) : InvalidOperationException(message);
