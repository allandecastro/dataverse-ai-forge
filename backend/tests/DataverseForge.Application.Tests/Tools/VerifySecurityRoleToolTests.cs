using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifySecurityRoleToolTests
{
    private readonly Mock<IDataverseClient> _mockDataverseClient = new();

    [Fact]
    public async Task VerifySecurityRole_Exists_ReturnPass()
    {
        var roleId = Guid.NewGuid();
        _mockDataverseClient
            .Setup(c => c.GetSecurityRoleDetailsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRoleDetails(roleId, "Work Order Contributor", false, []));

        var tool = new VerifySecurityRoleTool(_mockDataverseClient.Object);
        var result = await tool.VerifySecurityRoleAsync(roleId, "Work Order Contributor", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.Matches);
        Assert.Empty(result.Discrepancies);
        Assert.Equal("PASS", result.Verdict);
    }

    [Fact]
    public async Task VerifySecurityRole_NotFound_ReturnFail()
    {
        _mockDataverseClient
            .Setup(c => c.GetSecurityRoleDetailsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Not found"));

        var tool = new VerifySecurityRoleTool(_mockDataverseClient.Object);
        var result = await tool.VerifySecurityRoleAsync(Guid.NewGuid(), "NonExistent Role", CancellationToken.None);

        Assert.False(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual == "NOT FOUND");
    }

    [Fact]
    public async Task VerifySecurityRole_NameMismatch_ReturnFail()
    {
        var roleId = Guid.NewGuid();
        _mockDataverseClient
            .Setup(c => c.GetSecurityRoleDetailsAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRoleDetails(roleId, "Actual Role Name", false, []));

        var tool = new VerifySecurityRoleTool(_mockDataverseClient.Object);
        var result = await tool.VerifySecurityRoleAsync(roleId, "Expected Role Name", CancellationToken.None);

        Assert.True(result.Exists);
        Assert.False(result.Matches);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d =>
            d.Property == "Name" &&
            d.Expected == "Expected Role Name" &&
            d.Actual == "Actual Role Name");
    }
}
