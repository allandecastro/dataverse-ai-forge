using DataverseForge.Application.Contracts;
using DataverseForge.Application.Dtos;
using DataverseForge.Infrastructure.Dataverse;
using DataverseForge.Infrastructure.Settings;
using DataverseForge.Infrastructure.Stores;
using Azure.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Infrastructure.Tests.Connections;

/// <summary>Per-project Dataverse / Azure DevOps connections.</summary>
public sealed class ProjectConnectionTests : IDisposable
{
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private readonly ISecretProtector _protector = new AesGcmSecretProtector(TestKey);
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"forge-conn-{Guid.NewGuid():N}.db");
    private string ConnectionString => $"Data Source={_dbPath}";

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection(ConnectionString));
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(_dbPath + suffix)) File.Delete(_dbPath + suffix);
    }

    private static DataverseConnection Dv(string url = "https://contoso.crm.dynamics.com", string secret = "dv-secret") =>
        new(url, "ServicePrincipal", "tenant", "client", secret);

    // ─── Store ───

    [Fact]
    public async Task SqliteStore_RoundTrips_AndEncryptsSecretsAtRest()
    {
        var store = new SqliteProjectConnectionStore(ConnectionString, _protector);
        var connection = new ProjectConnection(Dv(), new AzureDevOpsConnection("https://dev.azure.com/c", "P", "azdo-pat"));

        await store.SaveAsync("p1", connection);

        Assert.Equal(connection, await store.GetAsync("p1"));
        Assert.Null(await store.GetAsync("p2"));

        // Secrets never hit the DB in clear text.
        await using var db = new SqliteConnection(ConnectionString);
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT connection_json FROM project_connections WHERE session_id = 'p1'";
        var raw = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.DoesNotContain("dv-secret", raw);
        Assert.DoesNotContain("azdo-pat", raw);
        Assert.Contains("enc:v1:", raw);
    }

    [Fact]
    public async Task SqliteStore_Delete_RemovesOnlyThatProject()
    {
        var store = new SqliteProjectConnectionStore(ConnectionString, _protector);
        await store.SaveAsync("p1", new ProjectConnection(Dv(), null));
        await store.SaveAsync("p2", new ProjectConnection(Dv("https://other.crm.dynamics.com"), null));

        await store.DeleteAsync("p1");

        Assert.Null(await store.GetAsync("p1"));
        Assert.NotNull(await store.GetAsync("p2"));
    }

    // ─── Resolver ───

    [Fact]
    public async Task Resolver_ReturnsTheCurrentProjectsConnection()
    {
        var store = new InMemoryProjectConnectionStore();
        await store.SaveAsync("a", new ProjectConnection(Dv("https://a.crm.dynamics.com"), null));
        await store.SaveAsync("b", new ProjectConnection(Dv("https://b.crm.dynamics.com"), null));

        var resolver = new DataverseConnectionResolver(new SessionContext { SessionId = "b" }, store);

        Assert.Equal("https://b.crm.dynamics.com", (await resolver.GetRequiredAsync()).EnvironmentUrl);
    }

    [Theory]
    [InlineData("no-connection")]
    [InlineData("")]
    public async Task Resolver_NoUsableConnection_ThrowsActionableError(string sessionId)
    {
        var store = new InMemoryProjectConnectionStore();
        var resolver = new DataverseConnectionResolver(new SessionContext { SessionId = sessionId }, store);

        var ex = await Assert.ThrowsAsync<ProjectConnectionMissingException>(() => resolver.GetRequiredAsync());
        Assert.Contains("Dataverse connection", ex.Message);
    }

    [Fact]
    public async Task Resolver_IncompleteServicePrincipal_IsNotUsable()
    {
        var store = new InMemoryProjectConnectionStore();
        await store.SaveAsync("p", new ProjectConnection(Dv(secret: ""), null));
        var resolver = new DataverseConnectionResolver(new SessionContext { SessionId = "p" }, store);

        await Assert.ThrowsAsync<ProjectConnectionMissingException>(() => resolver.GetRequiredAsync());
    }

    // ─── Credential cache ───

    [Fact]
    public void CredentialCache_KeepsOneEntryPerConnection()
    {
        var cache = new DataverseCredentialCache();
        var keyA = DataverseCredentialCache.KeyFor(Dv("https://a.crm.dynamics.com"));
        var keyB = DataverseCredentialCache.KeyFor(Dv("https://b.crm.dynamics.com"));
        var credA = Mock.Of<TokenCredential>();
        var credB = Mock.Of<TokenCredential>();

        cache.GetOrAddCredential(keyA, () => credA);
        cache.GetOrAddCredential(keyB, () => credB);
        cache.SetToken(keyA, new AccessToken("token-a", DateTimeOffset.UtcNow.AddHours(1)));
        cache.SetToken(keyB, new AccessToken("token-b", DateTimeOffset.UtcNow.AddHours(1)));

        // Project B's sign-in didn't evict project A's.
        Assert.Equal("token-a", cache.GetCachedToken(keyA)!.Value.Token);
        Assert.Equal("token-b", cache.GetCachedToken(keyB)!.Value.Token);
        Assert.Same(credA, cache.GetOrAddCredential(keyA, () => throw new InvalidOperationException("must be cached")));

        cache.Reset(keyA);
        Assert.Null(cache.GetCachedToken(keyA));
        Assert.Equal("token-b", cache.GetCachedToken(keyB)!.Value.Token);
    }

    [Fact]
    public void CredentialCache_RotatedSecret_GetsANewKey()
    {
        Assert.NotEqual(
            DataverseCredentialCache.KeyFor(Dv(secret: "old")),
            DataverseCredentialCache.KeyFor(Dv(secret: "new")));
        Assert.DoesNotContain("old", DataverseCredentialCache.KeyFor(Dv(secret: "old")));
    }

    // ─── Migration ───

    private (UserSettingsService Settings, string Path) SettingsWithLegacy(bool withLegacy)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"forge-mig-{Guid.NewGuid():N}.json");
        if (withLegacy)
        {
            File.WriteAllText(path, $$"""
            { "Dataverse": { "EnvironmentUrl": "https://legacy.crm.dynamics.com", "TenantId": "t", "ClientId": "c",
                             "ClientSecret": "{{_protector.Protect("legacy-secret")}}" } }
            """);
        }
        var svc = new UserSettingsService(new ConfigurationBuilder().Build(), NullLogger<UserSettingsService>.Instance, _protector, path);
        return (svc, path);
    }

    private static Mock<IProjectStateStore> Projects(params string[] ids)
    {
        var projects = new Mock<IProjectStateStore>();
        projects.Setup(p => p.ListSessionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ids
            .Select(id => new SessionSummary(id, id, "", "", "", 0, 0, DateTime.UtcNow, DateTime.UtcNow)).ToList());
        return projects;
    }

    [Fact]
    public async Task Migration_CopiesLegacyToProjectsWithoutConnection_ThenRemovesIt()
    {
        var (settings, path) = SettingsWithLegacy(withLegacy: true);
        try
        {
            var store = new InMemoryProjectConnectionStore();
            var ownConnection = new ProjectConnection(Dv("https://own.crm.dynamics.com"), null);
            await store.SaveAsync("already-configured", ownConnection);

            await ProjectConnectionMigration.RunAsync(settings, Projects("old-1", "old-2", "already-configured").Object,
                store, NullLogger.Instance);

            Assert.Equal("https://legacy.crm.dynamics.com", (await store.GetAsync("old-1"))!.Dataverse!.EnvironmentUrl);
            Assert.Equal("legacy-secret", (await store.GetAsync("old-2"))!.Dataverse!.ClientSecret);
            Assert.Equal(ownConnection, await store.GetAsync("already-configured"));
            Assert.Null(await settings.ReadLegacyConnectionAsync()); // runs once
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Migration_NoProjectsYet_KeepsLegacyForNextStart()
    {
        var (settings, path) = SettingsWithLegacy(withLegacy: true);
        try
        {
            await ProjectConnectionMigration.RunAsync(settings, Projects().Object,
                new InMemoryProjectConnectionStore(), NullLogger.Instance);

            Assert.NotNull(await settings.ReadLegacyConnectionAsync());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Migration_NoLegacy_DoesNothing()
    {
        var (settings, path) = SettingsWithLegacy(withLegacy: false);
        var store = new InMemoryProjectConnectionStore();

        await ProjectConnectionMigration.RunAsync(settings, Projects("p").Object, store, NullLogger.Instance);

        Assert.Null(await store.GetAsync("p"));
        Assert.False(File.Exists(path));
    }
}
