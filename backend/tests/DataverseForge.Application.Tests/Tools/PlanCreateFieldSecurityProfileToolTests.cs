using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateFieldSecurityProfileToolTests
{
    private readonly PlanCreateFieldSecurityProfileTool _tool = new();

    [Fact]
    public async Task PlanCreateFieldSecurityProfile_ValidInput_ReturnsPlan()
    {
        var plan = await _tool.PlanCreateFieldSecurityProfileAsync(
            profileName: "PII Access - HR Team",
            description: "Restricts PII field access to HR personnel",
            fieldPermissionsDefinition: "cr123_employee:cr123_salary:true:false:false;cr123_employee:cr123_ssn:true:false:false",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("PII Access - HR Team", plan.ProfileName);
        Assert.Equal("Restricts PII field access to HR personnel", plan.Description);
        Assert.Equal(2, plan.FieldPermissions.Count);
        Assert.NotEmpty(plan.SensitiveFieldsDetected);
        Assert.NotEmpty(plan.WebApiPayloadJson);
        Assert.NotEmpty(plan.Warnings);
        Assert.Equal("orange", plan.RiskLevel);
    }

    [Fact]
    public async Task PlanCreateFieldSecurityProfile_EmptyPermissions_Warning()
    {
        var plan = await _tool.PlanCreateFieldSecurityProfileAsync(
            profileName: "Empty Profile",
            description: "Profile with no permissions",
            fieldPermissionsDefinition: "",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w => w.Contains("No field permissions defined"));
    }

    [Fact]
    public async Task PlanCreateFieldSecurityProfile_DetectsPiiFields()
    {
        var plan = await _tool.PlanCreateFieldSecurityProfileAsync(
            profileName: "Financial Access",
            description: "Salary field access",
            fieldPermissionsDefinition: "cr123_employee:cr123_salary:true:false:false",
            CancellationToken.None);

        Assert.Single(plan.SensitiveFieldsDetected);
        Assert.Contains(plan.SensitiveFieldsDetected, s => s.Contains("Financial"));
    }

    [Fact]
    public async Task PlanCreateFieldSecurityProfile_WarnsFullAccessToPii()
    {
        var plan = await _tool.PlanCreateFieldSecurityProfileAsync(
            profileName: "Full PII Access",
            description: "Full access to email field",
            fieldPermissionsDefinition: "cr123_employee:cr123_email:true:true:true",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w =>
            w.Contains("Full access") && w.Contains("cr123_email"));
    }

    [Fact]
    public async Task PlanCreateFieldSecurityProfile_AlwaysWarnsIsSecuredPermanent()
    {
        var plan = await _tool.PlanCreateFieldSecurityProfileAsync(
            profileName: "Any Profile",
            description: "Any description",
            fieldPermissionsDefinition: "cr123_employee:cr123_name:true:false:false",
            CancellationToken.None);

        Assert.Contains(plan.Warnings, w =>
            w.Contains("IsSecured") && w.Contains("CANNOT be disabled"));
    }

    [Fact]
    public async Task PlanCreateFieldSecurityProfile_RiskLevelIsOrange()
    {
        var plan = await _tool.PlanCreateFieldSecurityProfileAsync(
            profileName: "Risk Level Profile",
            description: "Checking risk level",
            fieldPermissionsDefinition: "cr123_employee:cr123_name:true:false:false",
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
