using DataverseForge.Domain.Interfaces;
using DataverseForge.Infrastructure.Dataverse;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataverseForge.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDataverseClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DataverseOptions>(
            configuration.GetSection(DataverseOptions.SectionName));

        services.AddTransient<DataverseAuthHandler>();

        services.AddHttpClient<IDataverseClient, DataverseClient>((sp, client) =>
        {
            var options = configuration
                .GetSection(DataverseOptions.SectionName)
                .Get<DataverseOptions>()!;

            client.BaseAddress = new Uri(options.EnvironmentUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        })
        .AddHttpMessageHandler<DataverseAuthHandler>();

        return services;
    }
}
