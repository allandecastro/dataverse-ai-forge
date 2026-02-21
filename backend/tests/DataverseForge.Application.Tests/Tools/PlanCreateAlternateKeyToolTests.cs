using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateAlternateKeyToolTests
{
    private readonly PlanCreateAlternateKeyTool _tool = new();

    [Fact]
    public async Task PlanAlternateKey_ValidInput_ReturnsValidPlan()
    {
        var plan = await _tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "External Project ID",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_externalid",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_project", plan.TableLogicalName);
        Assert.Equal("cr123_externalprojectidkey", plan.KeyLogicalName);
        Assert.Single(plan.KeyAttributes);
        Assert.Equal("cr123_externalid", plan.KeyAttributes[0]);
        Assert.Contains("KeyAttributes", plan.WebApiPayloadJson);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanAlternateKey_MultipleAttributes_ParsesCorrectly()
    {
        var plan = await _tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "Composite Key",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_externalid, cr123_source, cr123_region",
            CancellationToken.None);

        Assert.Equal(3, plan.KeyAttributes.Count);
        Assert.Equal("cr123_externalid", plan.KeyAttributes[0]);
        Assert.Equal("cr123_source", plan.KeyAttributes[1]);
        Assert.Equal("cr123_region", plan.KeyAttributes[2]);
    }

    [Fact]
    public async Task PlanAlternateKey_TooManyAttributes_ReturnsWarning()
    {
        var plan = await _tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "Big Key",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_a,cr123_b,cr123_c,cr123_d,cr123_e,cr123_f",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("maximum of 5"));
    }

    [Fact]
    public async Task PlanAlternateKey_RiskLevel_IsYellow()
    {
        var plan = await _tool.PlanCreateAlternateKeyAsync(
            tableLogicalName: "cr123_project",
            keyDisplayName: "External ID",
            publisherPrefix: "cr123",
            keyAttributesCsv: "cr123_externalid",
            CancellationToken.None);

        Assert.Equal("yellow", plan.RiskLevel);
    }
}
