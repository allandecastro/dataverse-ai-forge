using DataverseForge.Infrastructure.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Infrastructure.Tests.Settings;

/// <summary>
/// Validation-path tests only. The HTTP probe paths require non-trivial
/// HttpClient stubbing — covered separately by integration tests.
/// </summary>
public class SettingsTestServiceTests
{
    private static SettingsTestService MakeService()
    {
        // The validation paths short-circuit before HttpClient is used; a strict
        // factory mock is fine — if validation falls through we'll fail loudly.
        var factory = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        return new SettingsTestService(factory.Object, NullLogger<SettingsTestService>.Instance);
    }

    [Fact]
    public async Task TestAsync_UnknownService_ReturnsFailureWithName()
    {
        var result = await MakeService().TestAsync("not-a-service", new SettingsDto());

        Assert.False(result.Success);
        Assert.Contains("Unknown service", result.Message);
        Assert.Contains("not-a-service", result.Message);
    }

    [Fact]
    public async Task TestAzureAi_RejectsEmptyEndpoint()
    {
        var result = await MakeService().TestAsync("azureai", new SettingsDto
        {
            AzureAiApiKey = "real-key",
            AzureAiEndpoint = "",
        });

        Assert.False(result.Success);
        Assert.Contains("Endpoint is required", result.Message);
    }

    [Fact]
    public async Task TestAzureAi_RejectsMaskedApiKey()
    {
        // Symptom this protects: user clicks "Test" without re-entering the key.
        // The frontend sends the masked value back; we reject it with a clear message
        // instead of attempting an HTTP call that would fail with 401.
        var result = await MakeService().TestAsync("azureai", new SettingsDto
        {
            AzureAiEndpoint = "https://contoso.openai.azure.com/",
            AzureAiApiKey = "sk-a••••••••",
        });

        Assert.False(result.Success);
        Assert.Contains("not masked", result.Message);
    }

    [Fact]
    public async Task TestAzureAi_RejectsEmptyApiKey()
    {
        var result = await MakeService().TestAsync("azureai", new SettingsDto
        {
            AzureAiEndpoint = "https://contoso.openai.azure.com/",
            AzureAiApiKey = "",
        });

        Assert.False(result.Success);
        Assert.Contains("API key", result.Message);
    }

    [Fact]
    public async Task TestAzureAi_AzureOpenAiEndpoint_RequiresModel()
    {
        // Azure OpenAI URLs require the deployment name in the path; without a model
        // we can't even build the probe URL. Reject early with a clear message.
        var result = await MakeService().TestAsync("azureai", new SettingsDto
        {
            AzureAiEndpoint = "https://contoso.cognitiveservices.azure.com/",
            AzureAiApiKey = "real-key",
            AzureAiDefaultModel = "",
        });

        Assert.False(result.Success);
        Assert.Contains("model", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestDataverse_RejectsMissingFields()
    {
        var result = await MakeService().TestAsync("dataverse", new SettingsDto
        {
            DataverseEnvironmentUrl = "",
            DataverseTenantId = "",
            DataverseClientId = "",
            DataverseClientSecret = "",
        });

        Assert.False(result.Success);
        Assert.NotEmpty(result.Message);
    }

    [Fact]
    public async Task TestAsync_IsCaseInsensitiveOnServiceName()
    {
        // The endpoint accepts service names in any casing; routing should
        // pick the same handler regardless of how the frontend sends it.
        var resultLower = await MakeService().TestAsync("azureai", new SettingsDto());
        var resultUpper = await MakeService().TestAsync("AzureAI", new SettingsDto());
        var resultMixed = await MakeService().TestAsync("AzureAi", new SettingsDto());

        // All three should follow the same code path → same failure message
        // (empty endpoint).
        Assert.False(resultLower.Success);
        Assert.False(resultUpper.Success);
        Assert.False(resultMixed.Success);
        Assert.Equal(resultLower.Message, resultUpper.Message);
        Assert.Equal(resultLower.Message, resultMixed.Message);
    }
}
