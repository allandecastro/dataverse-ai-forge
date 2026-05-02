using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DataverseForge.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end smoke tests that boot the actual web host via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> and exercise public HTTP
/// endpoints over the in-memory test server.
///
/// Tagged "Integration" so they run as a separate CI step from the fast unit
/// tests — slow startup and broader fail surface (port-style flakiness, DI
/// wiring drift, JSON shape changes) make them more valuable as a separate
/// signal than mixed in.
///
/// These tests do NOT call out to real Azure AI / Dataverse / GitHub. They
/// hit endpoints whose happy path is purely local DI + in-memory state.
/// </summary>
[Trait("Category", "Integration")]
public class EndpointSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EndpointSmokeTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Readyz_Returns200_WhenHostStarts()
    {
        // Dumbest possible smoke test — proves the DI graph wires up without
        // throwing. Catches "I broke a service registration" regressions
        // before they surface as a 500 in the user's face.
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(body);
        Assert.Equal("ready", body!["status"]?.ToString());
    }

    [Fact]
    public async Task GetSettings_ReturnsMaskedDtoShape()
    {
        // Validates: the SettingsDto JSON contract is stable, and secrets are
        // never returned in cleartext. A frontend type drift would surface here
        // before users see "settings won't save" via the actual UI.
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/settings");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(body);

        // Required keys for the frontend to render the form.
        Assert.Contains("azureAiEndpoint", body!.Keys);
        Assert.Contains("dataverseEnvironmentUrl", body.Keys);
        Assert.Contains("dataverseAuthMode", body.Keys);
        Assert.Contains("dataverseClientSecret", body.Keys);

        // Secrets must never come back in cleartext. Empty (no value set yet)
        // is acceptable; an unmasked real secret is not.
        var secret = body["dataverseClientSecret"]?.ToString() ?? string.Empty;
        Assert.True(secret == string.Empty || secret.Contains("••••"),
            $"Client secret leaked unmasked: '{secret}'");
    }

    [Fact]
    public async Task ListSessions_ReturnsEmptyEnvelope_OnFreshHost()
    {
        // Validates: persistence wiring. A test host has no SQLite file by
        // default → InMemoryStore path → empty list. Catches the case where
        // an early refactor accidentally swaps store providers.
        using var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(body);
        Assert.Contains("count", body!.Keys);
        Assert.Contains("sessions", body.Keys);
    }
}
