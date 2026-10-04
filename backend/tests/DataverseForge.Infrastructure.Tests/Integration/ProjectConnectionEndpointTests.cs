using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DataverseForge.Infrastructure.Tests.Integration;

/// <summary>
/// HTTP-level behaviour of /api/sessions/{id}/connection over the in-memory host:
/// masking, the environment lock, per-project isolation, cleanup on delete.
/// </summary>
[Trait("Category", "Integration")]
public class ProjectConnectionEndpointTests : IClassFixture<InMemoryWebAppFactory>
{
    private readonly InMemoryWebAppFactory _factory;

    public ProjectConnectionEndpointTests(InMemoryWebAppFactory factory) => _factory = factory;

    private static async Task<string> CreateProjectAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/sessions", new { projectName = name });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("sessionId").GetString()!;
    }

    private static object Connection(string url, string secret = "super-secret-value") => new
    {
        dataverseEnvironmentUrl = url,
        dataverseAuthMode = "ServicePrincipal",
        dataverseTenantId = "tenant",
        dataverseClientId = "client",
        dataverseClientSecret = secret,
    };

    private static async Task<JsonElement> GetAsync(HttpClient client, string id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}/connection");

    [Fact]
    public async Task NewProject_HasAnEmptyUnlockedConnection()
    {
        using var client = _factory.CreateClient();
        var id = await CreateProjectAsync(client, "Fresh");

        var conn = await GetAsync(client, id);

        Assert.Equal("", conn.GetProperty("dataverseEnvironmentUrl").GetString());
        Assert.False(conn.GetProperty("dataverseEnvironmentLocked").GetBoolean());
    }

    [Fact]
    public async Task Save_MasksSecret_AndLocksEnvironment()
    {
        using var client = _factory.CreateClient();
        var id = await CreateProjectAsync(client, "Client A");

        var put = await client.PutAsJsonAsync($"/api/sessions/{id}/connection", Connection("https://a.crm.dynamics.com/"));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var conn = await GetAsync(client, id);
        Assert.Equal("https://a.crm.dynamics.com", conn.GetProperty("dataverseEnvironmentUrl").GetString());
        Assert.True(conn.GetProperty("dataverseEnvironmentLocked").GetBoolean());
        var secret = conn.GetProperty("dataverseClientSecret").GetString()!;
        Assert.Contains("••••", secret);
        Assert.DoesNotContain("super-secret-value", secret);
    }

    [Fact]
    public async Task ChangingTheEnvironment_IsRejected_ButCredentialsCanRotate()
    {
        using var client = _factory.CreateClient();
        var id = await CreateProjectAsync(client, "Client B");
        await client.PutAsJsonAsync($"/api/sessions/{id}/connection", Connection("https://b.crm.dynamics.com"));

        var move = await client.PutAsJsonAsync($"/api/sessions/{id}/connection", Connection("https://other.crm.dynamics.com"));
        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);

        // Same environment, new secret: allowed.
        var rotate = await client.PutAsJsonAsync($"/api/sessions/{id}/connection",
            Connection("https://b.crm.dynamics.com", secret: "rotated-secret"));
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        Assert.StartsWith("rota", (await GetAsync(client, id)).GetProperty("dataverseClientSecret").GetString());
    }

    [Fact]
    public async Task MaskedSecret_OnSave_KeepsTheStoredOne()
    {
        using var client = _factory.CreateClient();
        var id = await CreateProjectAsync(client, "Client C");
        await client.PutAsJsonAsync($"/api/sessions/{id}/connection", Connection("https://c.crm.dynamics.com"));
        var masked = (await GetAsync(client, id)).GetProperty("dataverseClientSecret").GetString()!;

        // The UI sends back the masked value when the user only edits another field.
        await client.PutAsJsonAsync($"/api/sessions/{id}/connection", new
        {
            dataverseEnvironmentUrl = "https://c.crm.dynamics.com",
            dataverseAuthMode = "ServicePrincipal",
            dataverseTenantId = "new-tenant",
            dataverseClientId = "client",
            dataverseClientSecret = masked,
        });

        var conn = await GetAsync(client, id);
        Assert.Equal("new-tenant", conn.GetProperty("dataverseTenantId").GetString());
        Assert.Equal(masked, conn.GetProperty("dataverseClientSecret").GetString()); // still the original
    }

    [Fact]
    public async Task NonHttpsEnvironmentUrl_IsRejected()
    {
        using var client = _factory.CreateClient();
        var id = await CreateProjectAsync(client, "Bad URL");

        var res = await client.PutAsJsonAsync($"/api/sessions/{id}/connection", Connection("http://insecure.example"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Projects_AreIsolated_AndDeleteRemovesTheConnection()
    {
        using var client = _factory.CreateClient();
        var a = await CreateProjectAsync(client, "Iso A");
        var b = await CreateProjectAsync(client, "Iso B");
        await client.PutAsJsonAsync($"/api/sessions/{a}/connection", Connection("https://iso-a.crm.dynamics.com"));

        Assert.Equal("", (await GetAsync(client, b)).GetProperty("dataverseEnvironmentUrl").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/sessions/{a}")).StatusCode);
        Assert.Equal("", (await GetAsync(client, a)).GetProperty("dataverseEnvironmentUrl").GetString());
    }

    [Fact]
    public async Task Status_WithoutConnection_ReportsNotConfigured()
    {
        using var client = _factory.CreateClient();
        var id = await CreateProjectAsync(client, "No conn");

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{id}/connection/status");

        Assert.False(status.GetProperty("configured").GetBoolean());
    }
}
