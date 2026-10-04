using DataverseForge.Application.Contracts;
using DataverseForge.Infrastructure.Concurrency;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.Settings;

namespace DataverseForge.Api.Endpoints;

/// <summary>
/// A project's own Dataverse / Azure DevOps connection (see <see cref="IProjectConnectionStore"/>).
/// Secrets are masked on read; a masked or empty secret on write keeps the stored one.
/// The environment URL is fixed once set: the project was built against it.
/// </summary>
internal static class ProjectConnectionEndpoints
{
    public static IEndpointRouteBuilder MapProjectConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sessions/{sessionId}/connection", async (
            string sessionId, IProjectConnectionStore store, CancellationToken ct) =>
        {
            var connection = await store.GetAsync(sessionId, ct) ?? ProjectConnection.Empty;
            return Results.Ok(ProjectConnectionDto.From(connection));
        });

        app.MapPut("/api/sessions/{sessionId}/connection", async (
            string sessionId,
            ProjectConnectionDto dto,
            IProjectConnectionStore store,
            SessionLocks sessionLocks,
            CancellationToken ct) =>
        {
            // Same lock as a chat turn, so a running turn never sees a half-changed connection.
            using var sessionLock = await sessionLocks.TryAcquireAsync(sessionId, TimeSpan.FromSeconds(3), ct);
            if (sessionLock is null)
                return Results.Conflict(new { error = "This project is busy with another request. Try again in a moment." });

            var existing = await store.GetAsync(sessionId, ct) ?? ProjectConnection.Empty;
            var merged = dto.MergeInto(existing, out var error);
            if (error is not null)
                return Results.BadRequest(new { error });

            await store.SaveAsync(sessionId, merged, ct);
            return Results.Ok(ProjectConnectionDto.From(merged));
        });

        // Tests the values in the body (unsaved edits), with masked secrets filled in
        // from the stored connection — the same merge as PUT, without saving.
        app.MapPost("/api/sessions/{sessionId}/connection/test", async (
            string sessionId,
            ProjectConnectionDto dto,
            IProjectConnectionStore store,
            SettingsTestService testService,
            CancellationToken ct) =>
        {
            var existing = await store.GetAsync(sessionId, ct) ?? ProjectConnection.Empty;
            var merged = dto.MergeInto(existing, out var error);
            if (error is not null)
                return Results.BadRequest(new { error });

            var result = await TestDataverseAsync(merged.Dataverse, testService, ct);
            return Results.Ok(result);
        }).RequireRateLimiting("settings-test");

        // Live status of the project's Dataverse connection — drives the sidebar.
        app.MapGet("/api/sessions/{sessionId}/connection/status", async (
            string sessionId,
            IProjectConnectionStore store,
            SettingsTestService testService,
            CancellationToken ct) =>
        {
            var dataverse = (await store.GetAsync(sessionId, ct))?.Dataverse;
            if (dataverse is null || !dataverse.IsConfigured)
                return Results.Ok(new { status = "unavailable", configured = false });

            var result = await TestDataverseAsync(dataverse, testService, ct);
            return result.Success
                ? Results.Ok(new
                {
                    status = "connected",
                    configured = true,
                    environment = result.Environment,
                    environmentUrl = result.EnvironmentUrl,
                })
                : Results.Ok(new { status = "unavailable", configured = true, error = result.Message });
        });

        // Drops this project's cached credential (Interactive: the next call re-prompts
        // the browser sign-in, so the user can switch account).
        app.MapPost("/api/sessions/{sessionId}/connection/signout", async (
            string sessionId,
            IProjectConnectionStore store,
            DataverseCredentialCache cache,
            CancellationToken ct) =>
        {
            var dataverse = (await store.GetAsync(sessionId, ct))?.Dataverse;
            if (dataverse is not null)
                cache.Reset(DataverseCredentialCache.KeyFor(dataverse));
            return Results.Ok(new { message = "Signed out. The next Dataverse call will require re-authentication." });
        });

        return app;
    }

    private static async Task<TestConnectionResult> TestDataverseAsync(
        DataverseConnection? dataverse, SettingsTestService testService, CancellationToken ct)
    {
        if (dataverse is null || string.IsNullOrWhiteSpace(dataverse.EnvironmentUrl))
            return new TestConnectionResult(false, "Enter the environment URL first.");

        return await testService.TestAsync("dataverse", new SettingsDto
        {
            DataverseEnvironmentUrl = dataverse.EnvironmentUrl,
            DataverseAuthMode = dataverse.AuthMode,
            DataverseTenantId = dataverse.TenantId,
            DataverseClientId = dataverse.ClientId,
            DataverseClientSecret = dataverse.ClientSecret,
        }, ct);
    }
}

/// <summary>Wire shape of a project connection. Secrets are masked on the way out.</summary>
internal sealed record ProjectConnectionDto
{
    public string DataverseEnvironmentUrl { get; init; } = string.Empty;
    public string DataverseAuthMode { get; init; } = "ServicePrincipal";
    public string DataverseTenantId { get; init; } = string.Empty;
    public string DataverseClientId { get; init; } = string.Empty;
    public string DataverseClientSecret { get; init; } = string.Empty;

    /// <summary>True once an environment URL is saved: it can no longer change.</summary>
    public bool DataverseEnvironmentLocked { get; init; }

    public string AzureDevOpsOrganizationUrl { get; init; } = string.Empty;
    public string AzureDevOpsProject { get; init; } = string.Empty;
    public string AzureDevOpsPersonalAccessToken { get; init; } = string.Empty;

    public static ProjectConnectionDto From(ProjectConnection c) => new()
    {
        DataverseEnvironmentUrl = c.Dataverse?.EnvironmentUrl ?? string.Empty,
        DataverseAuthMode = c.Dataverse?.AuthMode is { Length: > 0 } mode ? mode : "ServicePrincipal",
        DataverseTenantId = c.Dataverse?.TenantId ?? string.Empty,
        DataverseClientId = c.Dataverse?.ClientId ?? string.Empty,
        DataverseClientSecret = Mask(c.Dataverse?.ClientSecret),
        DataverseEnvironmentLocked = !string.IsNullOrWhiteSpace(c.Dataverse?.EnvironmentUrl),
        AzureDevOpsOrganizationUrl = c.AzureDevOps?.OrganizationUrl ?? string.Empty,
        AzureDevOpsProject = c.AzureDevOps?.Project ?? string.Empty,
        AzureDevOpsPersonalAccessToken = Mask(c.AzureDevOps?.PersonalAccessToken),
    };

    /// <summary>
    /// Applies this DTO onto <paramref name="existing"/>. Masked or empty secrets keep
    /// the stored value. Returns an error (and <paramref name="existing"/>) when the
    /// edit is not allowed.
    /// </summary>
    public ProjectConnection MergeInto(ProjectConnection existing, out string? error)
    {
        error = null;
        var url = Normalize(DataverseEnvironmentUrl);
        var lockedUrl = Normalize(existing.Dataverse?.EnvironmentUrl);

        if (lockedUrl.Length > 0 && url.Length > 0 && !string.Equals(url, lockedUrl, StringComparison.OrdinalIgnoreCase))
        {
            error = $"This project is bound to {lockedUrl}; its environment can't be changed. " +
                    "Create a new project for another environment.";
            return existing;
        }

        var finalUrl = lockedUrl.Length > 0 ? lockedUrl : url;
        if (finalUrl.Length > 0
            && (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "The environment URL must be an absolute https:// URL (e.g. https://contoso.crm.dynamics.com).";
            return existing;
        }

        var dataverse = finalUrl.Length == 0 && existing.Dataverse is null
            ? null
            : new DataverseConnection(
                finalUrl,
                DataverseAuthMode is "Interactive" ? "Interactive" : "ServicePrincipal",
                DataverseTenantId.Trim(),
                DataverseClientId.Trim(),
                KeepIfMasked(DataverseClientSecret, existing.Dataverse?.ClientSecret));

        var azdoProvided = !string.IsNullOrWhiteSpace(AzureDevOpsOrganizationUrl) || !string.IsNullOrWhiteSpace(AzureDevOpsProject);
        var azureDevOps = !azdoProvided && existing.AzureDevOps is null
            ? null
            : new AzureDevOpsConnection(
                AzureDevOpsOrganizationUrl.Trim(),
                AzureDevOpsProject.Trim(),
                KeepIfMasked(AzureDevOpsPersonalAccessToken, existing.AzureDevOps?.PersonalAccessToken));

        return new ProjectConnection(dataverse, azureDevOps);
    }

    private static string Normalize(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');

    private static string KeepIfMasked(string incoming, string? stored) =>
        string.IsNullOrEmpty(incoming) || incoming.Contains("••••") ? stored ?? string.Empty : incoming;

    private static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (value.Length <= 4) return "••••";
        return value[..4] + "••••••••";
    }
}
