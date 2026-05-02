using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class CheckOobTableCoverageToolTests
{
    private static CheckOobTableCoverageTool MakeTool(params TableInfo[] tables)
    {
        var mock = new Mock<IDataverseClient>();
        mock.Setup(c => c.GetTablesListAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tables.ToList().AsReadOnly());
        return new CheckOobTableCoverageTool(mock.Object);
    }

    private static TableInfo Tbl(string logicalName, string displayName, bool managed = true, bool custom = false)
        => new(logicalName, displayName, displayName, custom, managed, "UserOwned",
               $"{logicalName}id", "name", false, false, false);

    [Fact]
    public async Task CheckOobTableCoverage_RecommendsAccount_WhenProposingCompany()
    {
        // The tool dynamically scores against tables present in the environment;
        // here we seed a realistic OOB-style "account" entry.
        var tool = MakeTool(
            Tbl("account", "Account"),
            Tbl("contact", "Contact"));

        var result = await tool.CheckOobTableCoverageAsync(
            "Company Account",
            "Stores company account information",
            ["name", "phone", "email", "address"],
            CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.Contains(result, r => r.OobTableName == "account");
        Assert.True(result.First(r => r.OobTableName == "account").MatchConfidence >= 25);
    }

    [Fact]
    public async Task CheckOobTableCoverage_NoOverlap_ReturnsEmpty()
    {
        var tool = MakeTool(
            Tbl("account", "Account"),
            Tbl("contact", "Contact"));

        var result = await tool.CheckOobTableCoverageAsync(
            "QuantumFluxCapacitor",
            "Tracks quantum flux readings from sensors",
            ["fluxlevel", "sensorid", "readingtime", "calibration"],
            CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task CheckOobTableCoverage_SkipsUnmanagedCustomTables()
    {
        // User's own unmanaged custom tables shouldn't be suggested as OOB candidates —
        // that would loop them back to themselves. Use a query that overlaps both
        // tables' tokens so we can verify the unmanaged one is filtered while the
        // managed one survives.
        var tool = MakeTool(
            Tbl("eppc_account", "Account", managed: false, custom: true),
            Tbl("account", "Account"));

        var result = await tool.CheckOobTableCoverageAsync(
            "Account",
            "Customer account",
            ["name"],
            CancellationToken.None);

        Assert.DoesNotContain(result, r => r.OobTableName == "eppc_account");
        Assert.Contains(result, r => r.OobTableName == "account");
    }

    [Fact]
    public async Task CheckOobTableCoverage_ManagedCustomTablesAreCandidates()
    {
        // Managed (vendor-shipped or industry-cloud) tables that happen to be marked
        // IsCustom should still be candidates — they're not the user's own work.
        var tool = MakeTool(
            Tbl("msdyn_workorder", "Work Order", managed: true, custom: true));

        var result = await tool.CheckOobTableCoverageAsync(
            "Work Order",
            "Service work orders",
            ["title", "description"],
            CancellationToken.None);

        Assert.Contains(result, r => r.OobTableName == "msdyn_workorder");
    }

    [Fact]
    public async Task CheckOobTableCoverage_RanksByConfidence()
    {
        var tool = MakeTool(
            Tbl("account", "Account"),
            Tbl("accountleads", "AccountLeads"),
            Tbl("contact", "Contact"));

        var result = await tool.CheckOobTableCoverageAsync(
            "Account",
            "Customer account",
            ["name"],
            CancellationToken.None);

        // Highest confidence should be "account" (exact name match) — not contact.
        Assert.Equal("account", result[0].OobTableName);
    }

    [Fact]
    public async Task CheckOobTableCoverage_TopNCap_AvoidsWallOfMatches()
    {
        // Seed many vaguely-matching tables — tool should cap at 10.
        var tables = Enumerable.Range(0, 30)
            .Select(i => Tbl($"contact_{i}", $"Contact{i}"))
            .ToArray();
        var tool = MakeTool(tables);

        var result = await tool.CheckOobTableCoverageAsync(
            "Contact",
            "People contacts",
            ["name"],
            CancellationToken.None);

        Assert.True(result.Count <= 10);
    }
}
