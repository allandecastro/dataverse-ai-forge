using DataverseForge.Application.Contracts;
using DataverseForge.Infrastructure.Stores;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataverseForge.Infrastructure.Tests.Integration;

/// <summary>
/// Boots the web host with in-memory persistence forced on, regardless of what
/// appsettings.json defaults to (now <c>Persistence:Provider=Sqlite</c>). Done by
/// swapping the store registrations at the DI level rather than via config — a
/// config override is subject to source-ordering with the app's own
/// appsettings load, whereas <see cref="ConfigureTestServices"/> is
/// deterministic. Without this the smoke tests would create (and, if
/// <c>DVFORGE_DATA_DIR</c> is set on the dev's shell, pollute) a real
/// <c>state.db</c>; they are meant to be "purely local DI + in-memory state".
/// </summary>
public sealed class InMemoryWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Force the "AI not configured" fallback deterministically. Program.cs
        // loads credentials from user-settings.json under ForgePaths.GetWritableDataDir(),
        // so on a dev machine that has configured Azure AI (via DVFORGE_DATA_DIR
        // OR an in-app data-dir pointer in the OS config dir) those real creds
        // leak in and the smoke test makes a live LLM call instead of streaming
        // the graceful fallback it asserts on. This in-memory source is applied
        // last, so it wins over the file. Same spirit as the in-memory store swap.
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAI:Endpoint"] = "",
                ["AzureAI:ApiKey"] = "",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProjectStateStore>();
            services.RemoveAll<IConversationStore>();
            services.AddSingleton<IProjectStateStore, InMemoryProjectStateStore>();
            services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        });
    }
}
