using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class CheckOobTableCoverageToolTests
{
    private readonly CheckOobTableCoverageTool _tool = new();

    [Fact]
    public async Task CheckOobTableCoverageAsync_Company_RecommendsAccount()
    {
        // Act
        var result = await _tool.CheckOobTableCoverageAsync(
            "Company",
            "Stores company information",
            ["name", "phone", "email", "address"],
            CancellationToken.None);

        // Assert
        Assert.NotEmpty(result);
        Assert.Equal("account", result[0].OobTableName);
        Assert.True(result[0].MatchConfidence > 20);
    }

    [Fact]
    public async Task CheckOobTableCoverageAsync_UniqueProposal_ReturnsEmptyOrLowConfidence()
    {
        // Act
        var result = await _tool.CheckOobTableCoverageAsync(
            "QuantumFluxCapacitor",
            "Tracks quantum flux readings from sensors",
            ["fluxlevel", "sensorid", "readingtime", "calibration"],
            CancellationToken.None);

        // Assert — no OOB table should match a completely unrelated concept
        Assert.Empty(result);
    }

    [Fact]
    public async Task CheckOobTableCoverageAsync_Ticket_RecommendsIncident()
    {
        // Act
        var result = await _tool.CheckOobTableCoverageAsync(
            "Ticket",
            "Customer support tickets",
            ["title", "description", "priority", "status"],
            CancellationToken.None);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains(result, r => r.OobTableName == "incident");
    }
}
