using DataverseForge.Application.Agents;
using DataverseForge.Infrastructure.AzureAI;
using Microsoft.Extensions.Logging.Abstractions;

namespace DataverseForge.Infrastructure.Tests.AzureAI;

/// <summary>
/// Lifecycle tests for <see cref="ChatClientFactory"/> — covers the F-47 disposal
/// edit + the F-51 disposed-state guards on <c>GetClientForAgent</c> /
/// <c>DefaultClient</c>. Real HTTP / SDK construction is gated by a placeholder
/// endpoint check, so the factory stays in the unconfigured branch and we can
/// exercise the lifecycle without standing up Azure.
/// </summary>
public class ChatClientFactoryTests
{
    private static AzureAiOptions UnconfiguredOptions() => new()
    {
        // Empty endpoint triggers the "not configured" guard inside BuildClients —
        // no SDK pipeline is created so disposal stays a logical assertion.
        Endpoint = string.Empty,
        ApiKey = string.Empty,
        DefaultModel = string.Empty,
    };

    [Fact]
    public void GetClientForAgent_Throws_When_NotConfigured()
    {
        using var factory = new ChatClientFactory(UnconfiguredOptions(), NullLogger<ChatClientFactory>.Instance);

        Assert.False(factory.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => factory.GetClientForAgent(AgentNames.SolutionArchitect));
    }

    [Fact]
    public void DefaultClient_Throws_When_NotConfigured()
    {
        using var factory = new ChatClientFactory(UnconfiguredOptions(), NullLogger<ChatClientFactory>.Instance);

        Assert.Throws<InvalidOperationException>(() => factory.DefaultClient);
    }

    [Fact]
    public void GetClientForAgent_Throws_ObjectDisposed_AfterDispose()
    {
        var factory = new ChatClientFactory(UnconfiguredOptions(), NullLogger<ChatClientFactory>.Instance);
        factory.Dispose();

        // F-51 guard: post-Dispose calls must surface ObjectDisposedException, not
        // the misleading KeyNotFoundException that would come from probing the
        // freshly emptied client dictionary.
        Assert.Throws<ObjectDisposedException>(() => factory.GetClientForAgent(AgentNames.SolutionArchitect));
    }

    [Fact]
    public void DefaultClient_Throws_ObjectDisposed_AfterDispose()
    {
        var factory = new ChatClientFactory(UnconfiguredOptions(), NullLogger<ChatClientFactory>.Instance);
        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => factory.DefaultClient);
    }

    [Fact]
    public void Reconfigure_Throws_ObjectDisposed_AfterDispose()
    {
        var factory = new ChatClientFactory(UnconfiguredOptions(), NullLogger<ChatClientFactory>.Instance);
        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => factory.Reconfigure(UnconfiguredOptions()));
    }

    [Fact]
    public void Dispose_Is_Idempotent()
    {
        var factory = new ChatClientFactory(UnconfiguredOptions(), NullLogger<ChatClientFactory>.Instance);

        factory.Dispose();
        // Second Dispose must be a no-op — DI will call Dispose on shutdown even
        // if a previous code path already disposed it (e.g. during a Reconfigure
        // failure path), so a throwing second call would surface as a noisy
        // shutdown error in production.
        var ex = Record.Exception(() => factory.Dispose());
        Assert.Null(ex);
    }
}
