using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateManyToManyRelationshipToolTests
{
    private readonly PlanCreateManyToManyRelationshipTool _tool = new();

    [Fact]
    public async Task PlanM2M_ValidInput_ReturnsValidPlanWithErdFragment()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_student",
            secondTable: "cr123_course",
            publisherPrefix: "cr123",
            relationshipSchemaName: null,
            intersectTableName: null,
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_student", plan.FirstTable);
        Assert.Equal("cr123_course", plan.SecondTable);
        Assert.Equal("cr123_student_course", plan.RelationshipSchemaName);
        Assert.Equal("cr123_student_course", plan.IntersectTableName);
        Assert.Contains("}o--o{", plan.ErDiagramFragment);
        Assert.Contains("Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata", plan.WebApiPayloadJson);
    }

    [Fact]
    public async Task PlanM2M_CustomNames_UsesProvidedNames()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_student",
            secondTable: "cr123_course",
            publisherPrefix: "cr123",
            relationshipSchemaName: "cr123_enrollment",
            intersectTableName: "cr123_enrollment_intersect",
            CancellationToken.None);

        Assert.Equal("cr123_enrollment", plan.RelationshipSchemaName);
        Assert.Equal("cr123_enrollment_intersect", plan.IntersectTableName);
    }

    [Fact]
    public async Task PlanM2M_SelfReferencing_ReturnsWarning()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_employee",
            secondTable: "cr123_employee",
            publisherPrefix: "cr123",
            relationshipSchemaName: null,
            intersectTableName: null,
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("Self-referencing"));
    }

    [Fact]
    public async Task PlanM2M_RiskLevel_IsOrange()
    {
        var plan = await _tool.PlanCreateManyToManyRelationshipAsync(
            firstTable: "cr123_student",
            secondTable: "cr123_course",
            publisherPrefix: "cr123",
            relationshipSchemaName: null,
            intersectTableName: null,
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
