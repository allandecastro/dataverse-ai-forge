using DataverseForge.Infrastructure.Stores;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseForge.Infrastructure.Tests.Stores;

public class SqliteTranscriptStoreTests
{
    // Shared-cache in-memory DB so connection-per-op sees the same data.
    private static SqliteTranscriptStore NewStore()
    {
        var conn = $"Data Source=file:transcript_{Guid.NewGuid():N}?mode=memory&cache=shared";
        var stateStore = new SqliteProjectStateStore(conn, NullLogger<SqliteProjectStateStore>.Instance);
        return new SqliteTranscriptStore(conn, stateStore);
    }

    [Fact]
    public async Task GetAsync_NoRow_ReturnsEmptyArray()
    {
        var store = NewStore();
        Assert.Equal("[]", await store.GetAsync("missing"));
    }

    [Fact]
    public async Task SaveAsync_ThenGet_RoundTrips()
    {
        var store = NewStore();
        const string json = "[{\"id\":\"m1\",\"role\":\"user\",\"content\":\"hi\"}]";

        await store.SaveAsync("s1", json);

        Assert.Equal(json, await store.GetAsync("s1"));
    }

    [Fact]
    public async Task SaveAsync_Overwrites()
    {
        var store = NewStore();
        await store.SaveAsync("s1", "[{\"id\":\"a\"}]");
        await store.SaveAsync("s1", "[{\"id\":\"b\"}]");

        Assert.Equal("[{\"id\":\"b\"}]", await store.GetAsync("s1"));
    }
}
