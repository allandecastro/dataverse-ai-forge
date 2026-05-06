using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetSecurityRolesToolTests
{
    private static GetSecurityRolesTool BuildTool(IDataverseClient client) =>
        new(client, NullLogger<GetSecurityRolesTool>.Instance);

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

        var tool = BuildTool(client.Object);
        var result = await tool.GetSecurityRolesAsync("sales", buGuid.ToString(), CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Single(result.Roles);
        Assert.Equal("Sales Person", result.Roles[0].Name);
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

        var tool = BuildTool(client.Object);
        var result = await tool.GetSecurityRolesAsync(null, "Default Business Unit", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Empty(result.Roles);
        // BU filter dropped to null because the input wasn't a parseable GUID.
        client.Verify(c => c.GetSecurityRolesAsync(null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSecurityRoles_NullBusinessUnitId_DoesNotFilter()
    {
        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetSecurityRolesAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SecurityRoleInfo>());

        var tool = BuildTool(client.Object);
        await tool.GetSecurityRolesAsync(null, null, CancellationToken.None);

        client.Verify(c => c.GetSecurityRolesAsync(null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSecurityRoles_OnClientException_SurfacesError()
    {
        // The whole point of the wrapper: instead of letting the AI framework
        // wrap any exception as "Function failed.", we catch it and put the
        // real Dataverse error message in result.Error.
        var client = new Mock<IDataverseClient>();
        client.Setup(c => c.GetSecurityRolesAsync(It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("List security roles failed: HTTP 403 — Privilege prvReadRole missing."));

        var tool = BuildTool(client.Object);
        var result = await tool.GetSecurityRolesAsync(null, null, CancellationToken.None);

        Assert.Empty(result.Roles);
        Assert.NotNull(result.Error);
        Assert.Contains("403", result.Error);
        Assert.Contains("prvReadRole", result.Error);
    }
}
