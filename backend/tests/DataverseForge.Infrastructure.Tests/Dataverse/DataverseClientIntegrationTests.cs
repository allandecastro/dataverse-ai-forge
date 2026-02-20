using DataverseForge.Domain.Interfaces;
using DataverseForge.Infrastructure;
using DataverseForge.Infrastructure.Dataverse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataverseForge.Infrastructure.Tests.Dataverse;

[Trait("Category", "Integration")]
public class DataverseClientIntegrationTests
{
    private readonly IDataverseClient _client;
    private readonly bool _isConfigured;

    public DataverseClientIntegrationTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.test.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = configuration
            .GetSection(DataverseOptions.SectionName)
            .Get<DataverseOptions>();

        _isConfigured = options is not null
            && !string.IsNullOrWhiteSpace(options.EnvironmentUrl)
            && options.EnvironmentUrl != "https://YOUR-ORG.crm.dynamics.com";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataverseClient(configuration);

        var provider = services.BuildServiceProvider();
        _client = provider.GetRequiredService<IDataverseClient>();
    }

    [SkippableFact]
    public async Task GetEnvironmentInfoAsync_ReturnsValidData()
    {
        Skip.IfNot(_isConfigured, "Dataverse credentials not configured");

        var info = await _client.GetEnvironmentInfoAsync();

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.OrganizationName));
        Assert.False(string.IsNullOrWhiteSpace(info.EnvironmentUrl));
        Assert.False(string.IsNullOrWhiteSpace(info.DataverseVersion));
        Assert.True(info.BaseLanguageLcid > 0);
    }

    [SkippableFact]
    public async Task GetSolutionsListAsync_ReturnsAtLeastOneSolution()
    {
        Skip.IfNot(_isConfigured, "Dataverse credentials not configured");

        var solutions = await _client.GetSolutionsListAsync();

        Assert.NotNull(solutions);
        Assert.NotEmpty(solutions);

        var first = solutions[0];
        Assert.NotEqual(Guid.Empty, first.SolutionId);
        Assert.False(string.IsNullOrWhiteSpace(first.UniqueName));
    }
}
