using DataverseForge.Application.Contracts;
using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class VerifySecurityRoleAgainstPlanToolTests
{
    private const string PlanId = "create_security_role_test1";
    private static readonly Guid RoleId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid BuId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid PrivReadId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static readonly string PayloadJson = $$"""
        {
          "role": {
            "name": "Feedback Reviewer",
            "description": "",
            "businessunitid@odata.bind": "/businessunits({{BuId}})"
          },
          "privileges": [
            { "PrivilegeId": "{{PrivReadId}}", "Depth": "Local", "Name": "prvReadadc_feedback" }
          ]
        }
        """;

    private readonly Mock<IDataverseClient> _mockDataverseClient = new();
    private readonly Mock<IProjectStateStore> _mockProjectStateStore = new();
    private readonly Mock<ISessionContext> _mockSessionContext = new();

    private VerifySecurityRoleAgainstPlanTool CreateTool() =>
        new(_mockDataverseClient.Object, _mockProjectStateStore.Object, _mockSessionContext.Object);

    private ProjectState SetupSession(string sessionId, Guid? roleIdOnStack)
    {
        var state = new ProjectState { ProjectId = sessionId };
        state.ExecutedPlans[PlanId] = PayloadJson;
        if (roleIdOnStack is not null)
        {
            state.OperationStack.Push(new ExecutedOperation
            {
                OperationId = Guid.NewGuid().ToString("N"),
                ExecutedAt = DateTime.UtcNow,
                AgentName = "SecurityBuilder",
                ToolName = "ExecuteCreateSecurityRole",
                Result = new { roleId = roleIdOnStack.Value, planId = PlanId },
            });
        }
        _mockSessionContext.Setup(sc => sc.SessionId).Returns(sessionId);
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);
        return state;
    }

    [Fact]
    public async Task VerifyCreatePlan_LiveMatches_ReturnsPass()
    {
        SetupSession("verify-session-1", RoleId);
        _mockDataverseClient.Setup(c => c.GetSecurityRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRoleDetails(
                Info: new SecurityRoleInfo(RoleId, "Feedback Reviewer", null, BuId, "Root", false, false, null, null, null),
                ParentRoleId: null,
                IsCustomizable: true));
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivReadId, "Local", "prvReadadc_feedback"),
            });

        var result = await CreateTool().VerifySecurityRoleAgainstPlanAsync(PlanId, CancellationToken.None);

        Assert.Equal("PASS", result.Verdict);
        Assert.Equal(RoleId, result.RoleId);
        Assert.Empty(result.Discrepancies);
    }

    [Fact]
    public async Task VerifyCreatePlan_PrivilegeMissing_ReturnsFail()
    {
        SetupSession("verify-session-2", RoleId);
        _mockDataverseClient.Setup(c => c.GetSecurityRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRoleDetails(
                Info: new SecurityRoleInfo(RoleId, "Feedback Reviewer", null, BuId, "Root", false, false, null, null, null),
                ParentRoleId: null,
                IsCustomizable: true));
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RolePrivilegeAssignment>());

        var result = await CreateTool().VerifySecurityRoleAgainstPlanAsync(PlanId, CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual == "<missing>");
    }

    [Fact]
    public async Task VerifyCreatePlan_NameMismatch_ReturnsFail()
    {
        SetupSession("verify-session-3", RoleId);
        _mockDataverseClient.Setup(c => c.GetSecurityRoleAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SecurityRoleDetails(
                Info: new SecurityRoleInfo(RoleId, "Wrong Name", null, BuId, "Root", false, false, null, null, null),
                ParentRoleId: null,
                IsCustomizable: true));
        _mockDataverseClient.Setup(c => c.GetRolePrivilegesAsync(RoleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RolePrivilegeAssignment>
            {
                new(PrivReadId, "Local", "prvReadadc_feedback"),
            });

        var result = await CreateTool().VerifySecurityRoleAgainstPlanAsync(PlanId, CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Property == "Name" && d.Actual == "Wrong Name");
    }

    [Fact]
    public async Task VerifyWrongPrefix_ReturnsFail()
    {
        SetupSession("verify-session-4", RoleId);
        var result = await CreateTool().VerifySecurityRoleAgainstPlanAsync(
            "create_field_xyz", CancellationToken.None);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual!.Contains("WRONG PLAN TYPE"));
    }

    [Fact]
    public async Task VerifyMissingExecutedPlan_ReturnsFail()
    {
        var state = new ProjectState { ProjectId = "verify-session-5" };
        _mockSessionContext.Setup(sc => sc.SessionId).Returns("verify-session-5");
        _mockProjectStateStore.Setup(s => s.GetOrCreateAsync("verify-session-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(state);

        var result = await CreateTool().VerifySecurityRoleAgainstPlanAsync(
            "create_security_role_doesnotexist", CancellationToken.None);

        Assert.Equal("FAIL", result.Verdict);
        Assert.Contains(result.Discrepancies, d => d.Actual == "NOT FOUND in ExecutedPlans");
    }
}
