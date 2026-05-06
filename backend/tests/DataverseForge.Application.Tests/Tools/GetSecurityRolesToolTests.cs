using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetSecurityRolesToolTests
{
    [Fact]
    public async Task GetSecurityRoles_DelegatesAndPassesGuidFilter()
    {
        var client = new Mock<IDataverseClient>();
        var buGuid = Guid.NewGuid();
        var expected = new List<SecurityRoleInfo>
        {
            new(Guid.NewGuid(), "Sales Person", null, buGuid, "Default Business Unit",
                IsManaged: true, IsAutoAssigned: false, AppliesTo: null, RoleTemplateId: null,
                ModifiedOn: DateTime.UtcNow),
        };
        client.Setup(c => c.GetSecurityRolesAsync("sales", buGuid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetSecurityRolesTool(client.Object);
        var result = await tool.GetSecurityRolesAsync("sales", buGuid.ToString(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Sales Person", result[0].Name);
        client.Verify(c => c.GetSecurityRolesAsync("sales", buGuid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSecurityRoles_SilentlyIgnoresInvalidBusinessUnitGuid()
    {
        // Real failure mode: the agent sometimes passes a BU NAME instead of
        // an id. Better to surface "no roles match" than block the call.
        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetSecurityRolesAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityRoleInfo>());

        var tool = new GetSecurityRolesTool(client.Object);
        var result = await tool.GetSecurityRolesAsync(null, "Default Business Unit", CancellationToken.None);

        Assert.Empty(result);
        // BU filter dropped to null because the input wasn't a parseable GUID.
        client.Verify(c => c.GetSecurityRolesAsync(null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSecurityRoles_NullBusinessUnitId_DoesNotFilter()
    {
        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetSecurityRolesAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityRoleInfo>());

        var tool = new GetSecurityRolesTool(client.Object);
        await tool.GetSecurityRolesAsync(null, null, CancellationToken.None);

        client.Verify(c => c.GetSecurityRolesAsync(null, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
