using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateSecurityRoleToolTests
{
    private readonly PlanCreateSecurityRoleTool _tool = new();

    [Fact]
    public async Task PlanCreateSecurityRole_ValidInput_ReturnsPlan()
    {
        var plan = await _tool.PlanCreateSecurityRoleAsync(
            roleName: "Work Order Contributor",
            description: "Can create and edit work orders",
            copyFromRoleId: null,
            privilegesDefinition: "cr123_workorder:businessUnit:organization:businessUnit:user:businessUnit:businessUnit:businessUnit:businessUnit",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("Work Order Contributor", plan.RoleName);
        Assert.Equal("Can create and edit work orders", plan.Description);
        Assert.Single(plan.Privileges);
        Assert.Equal("cr123_workorder", plan.Privileges[0].TableName);
        Assert.NotEmpty(plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanCreateSecurityRole_EmptyPrivileges_Warning()
    {
        var plan = await _tool.PlanCreateSecurityRoleAsync(
            roleName: "Empty Role",
            description: "No privileges",
            copyFromRoleId: null,
            privilegesDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("No privileges defined"));
    }

    [Fact]
    public async Task PlanCreateSecurityRole_OrgLevelDelete_Warning()
    {
        var plan = await _tool.PlanCreateSecurityRoleAsync(
            roleName: "Dangerous Delete Role",
            description: "Has org-level delete",
            copyFromRoleId: null,
            privilegesDefinition: "cr123_workorder:user:user:user:organization:user:user:user:user",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w =>
            w.Contains("Organization-level Delete") && w.Contains("cr123_workorder"));
    }

    [Fact]
    public async Task PlanCreateSecurityRole_OrgLevelAssign_Warning()
    {
        var plan = await _tool.PlanCreateSecurityRoleAsync(
            roleName: "Dangerous Assign Role",
            description: "Has org-level assign",
            copyFromRoleId: null,
            privilegesDefinition: "cr123_workorder:user:user:user:user:user:user:organization:user",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w =>
            w.Contains("Organization-level Assign") && w.Contains("cr123_workorder"));
    }

    [Fact]
    public async Task PlanCreateSecurityRole_PrivilegeMatrixContainsTable()
    {
        var plan = await _tool.PlanCreateSecurityRoleAsync(
            roleName: "Matrix Role",
            description: "Check matrix",
            copyFromRoleId: null,
            privilegesDefinition: "cr123_workorder:businessUnit:organization:businessUnit:user:businessUnit:businessUnit:businessUnit:businessUnit",
            CancellationToken.None);

        Assert.Contains("| Table |", plan.PrivilegeMatrix);
        Assert.Contains("cr123_workorder", plan.PrivilegeMatrix);
    }

    [Fact]
    public async Task PlanCreateSecurityRole_RiskLevelIsOrange()
    {
        var plan = await _tool.PlanCreateSecurityRoleAsync(
            roleName: "Any Role",
            description: "Check risk level",
            copyFromRoleId: null,
            privilegesDefinition: "cr123_workorder:user:user:user:user:user:user:user:user",
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateSecurityRole_CopyFromRoleId_Parsed()
    {
        var copyId = Guid.NewGuid();

        var plan = await _tool.PlanCreateSecurityRoleAsync(
            roleName: "Cloned Role",
            description: "Cloned from existing",
            copyFromRoleId: copyId.ToString(),
            privilegesDefinition: "cr123_workorder:user:user:user:user:user:user:user:user",
            CancellationToken.None);

        Assert.NotNull(plan.CopyFromRoleId);
        Assert.Equal(copyId, plan.CopyFromRoleId.Value);
    }
}
