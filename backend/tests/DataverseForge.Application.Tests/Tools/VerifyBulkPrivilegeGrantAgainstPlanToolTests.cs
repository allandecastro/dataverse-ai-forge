using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifyBulkPrivilegeGrantAgainstPlanToolTests
{
    private const string PlanId = "grant_privilege_to_roles_test";
    private static readonly Guid PrivId = Guid.Parse("11112222-3333-4444-5555-666677778888");
    private static readonly Guid RoleAId = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    private static readonly Guid RoleBId = Guid.Parse("bbbb2222-2222-2222-2222-222222222222");

    private static readonly string PayloadJson = $$"""
        {
          "privilege": {
            "PrivilegeId": "{{PrivId}}",
            "Depth": "Local",
            "Name": "prvReadAccount",
            "AccessRight": "Read"
          },
          "roleIds": ["{{RoleAId}}", "{{RoleBId}}"]
        }
        """;

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private VerifyBulkPrivilegeGrantAgainstPlanTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupSession(string sessionId)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.ExecutedPlans[PlanId] = PayloadJson;
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task VerifyBulk_AllRolesHavePrivilege_ReturnsPass()
    {
        SetupSession("verify-bulk-1");
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivId, "Local", "prvReadAccount"),
            });
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivId, "Local", "prvReadAccount"),
            });

        var result = await CreateTool().VerifyBulkPrivilegeGrantAgainstPlanAsync(PlanId, CancellationToken.None);

        Assert.Equal("PASS", result.OverallVerdict);
        Assert.Equal(2, result.PerRoleResults.Count);
        Assert.All(result.PerRoleResults, r => Assert.Equal("PASS", r.Verdict));
    }

    [Fact]
    public async Task VerifyBulk_OneRoleMissingPrivilege_ReturnsFail()
    {
        SetupSession("verify-bulk-2");
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivId, "Local", "prvReadAccount"),
            });
        // RoleB returns NO privileges — the bulk verdict must FAIL.
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RolePrivilegeAssignment>());

        var result = await CreateTool().VerifyBulkPrivilegeGrantAgainstPlanAsync(PlanId, CancellationToken.None);

        Assert.Equal("FAIL", result.OverallVerdict);
        Assert.Equal(2, result.PerRoleResults.Count);
        Assert.Equal("PASS", result.PerRoleResults.First(r => r.RoleId == RoleAId).Verdict);
        Assert.Equal("FAIL", result.PerRoleResults.First(r => r.RoleId == RoleBId).Verdict);
    }

    [Fact]
    public async Task VerifyBulk_DepthMismatchOnOneRole_ReturnsFail()
    {
        SetupSession("verify-bulk-3");
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleAId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivId, "Local", "prvReadAccount"),
            });
        // RoleB has the privilege but at User (Basic) depth — plan asked for BU (Local).
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleBId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivId, "Basic", "prvReadAccount"),
            });

        var result = await CreateTool().VerifyBulkPrivilegeGrantAgainstPlanAsync(PlanId, CancellationToken.None);

        Assert.Equal("FAIL", result.OverallVerdict);
        var roleB = result.PerRoleResults.First(r => r.RoleId == RoleBId);
        Assert.Equal("FAIL", roleB.Verdict);
        Assert.Contains(roleB.Discrepancies, d => d.Property.EndsWith("Depth", StringComparison.Ordinal));
    }

    [Fact]
    public async Task VerifyBulk_WrongPrefix_ReturnsFail()
    {
        SetupSession("verify-bulk-4");
        var result = await CreateTool().VerifyBulkPrivilegeGrantAgainstPlanAsync(
            "create_security_role_xyz", CancellationToken.None);

        Assert.Equal("FAIL", result.OverallVerdict);
        Assert.Contains(result.PerRoleResults.SelectMany(r => r.Discrepancies),
            d => d.Actual!.Contains("WRONG PLAN TYPE"));
    }
}
