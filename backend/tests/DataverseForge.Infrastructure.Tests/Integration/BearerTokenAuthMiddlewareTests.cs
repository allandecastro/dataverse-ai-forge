using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DataverseForge.Infrastructure.Tests.Integration;

/// <summary>
/// End-to-end coverage for <c>BearerTokenAuthMiddleware</c> via the real host.
///
/// The middleware is opt-in (no-op until <c>Auth:SharedSecret</c> is set), so
/// the matrix worth covering is: secret-unset (legacy dev), secret-set without
/// header, with wrong header, with right header, plus the explicit non-gated
/// paths (<c>/readyz</c>) which must keep working even when auth is on.
/// </summary>
[Trait("Category", "Integration")]
public class BearerTokenAuthMiddlewareTests
{
    private const string Secret = "test-secret-do-not-use-in-prod";

    private static WebApplicationFactory<Program> FactoryWith(string? sharedSecret)
    {
        // InMemoryWebAppFactory swaps the stores to in-memory (no state.db).
        return new InMemoryWebAppFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Auth:SharedSecret"] = sharedSecret,
                });
            });
        });
    }

    [Fact]
    public async Task NoSecretConfigured_ApiCall_PassesThrough()
    {
        // Default localhost-dev mode: secret is unset → middleware is a no-op
        // → /api/sessions answers normally without an Authorization header.
        // This is the regression case we must never break.
        await using var factory = FactoryWith(sharedSecret: null);
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task SecretConfigured_NoAuthHeader_Returns401()
    {
        await using var factory = FactoryWith(Secret);
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains(res.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task SecretConfigured_WrongBearer_Returns401()
    {
        await using var factory = FactoryWith(Secret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-secret");

        var res = await client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task SecretConfigured_NonBearerScheme_Returns401()
    {
        // Basic auth (or any non-Bearer scheme) must be rejected — the
        // middleware specifically checks for the "Bearer " prefix.
        await using var factory = FactoryWith(Secret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "anything");

        var res = await client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task SecretConfigured_CorrectBearer_PassesThrough()
    {
        await using var factory = FactoryWith(Secret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);

        var res = await client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task SecretConfigured_ReadyzStillOpen_NoAuthRequired()
    {
        // /readyz lives outside /api/* so platform probes (k8s, docker compose)
        // keep working without a token. The middleware filters on path prefix.
        await using var factory = FactoryWith(Secret);
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/readyz");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task SecretConfigured_WhitespaceOnly_TreatedAsUnset()
    {
        // "   " in config should not lock down the API — that would be a
        // surprising failure mode if someone half-fills the value in
        // appsettings.json. IsNullOrWhiteSpace makes this explicit.
        await using var factory = FactoryWith("   ");
        using var client = factory.CreateClient();

        var res = await client.GetAsync("/api/sessions");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
