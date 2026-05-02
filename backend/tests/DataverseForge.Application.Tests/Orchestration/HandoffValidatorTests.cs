using DataverseForge.Application.Orchestration;
using DataverseForge.Application.Agents;

namespace DataverseForge.Application.Tests.Orchestration;

public class HandoffValidatorTests
{
    [Fact]
    public void SA_CanHandoffToAnyAgent()
    {
        var targets = new[]
        {
            AgentNames.BusinessAnalyst,
            AgentNames.DataModelBuilder,
            AgentNames.UIBuilder,
            AgentNames.DataPopulation,
            AgentNames.Reviewer,
            AgentNames.Tester
        };

        foreach (var target in targets)
        {
            var result = HandoffValidator.Validate(AgentNames.SolutionArchitect, target);
            Assert.True(result.IsValid, $"SA → {target} should be valid");
        }
    }

    [Theory]
    [InlineData(AgentNames.DataModelBuilder)]
    [InlineData(AgentNames.UIBuilder)]
    [InlineData(AgentNames.DataPopulation)]
    public void Builder_CanHandoffToSA(string builder)
    {
        var result = HandoffValidator.Validate(builder, AgentNames.SolutionArchitect);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(AgentNames.DataModelBuilder)]
    [InlineData(AgentNames.UIBuilder)]
    [InlineData(AgentNames.DataPopulation)]
    public void Builder_CanHandoffToReviewer(string builder)
    {
        var result = HandoffValidator.Validate(builder, AgentNames.Reviewer);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(AgentNames.DataModelBuilder)]
    [InlineData(AgentNames.UIBuilder)]
    [InlineData(AgentNames.DataPopulation)]
    public void Builder_CanHandoffToTester(string builder)
    {
        var result = HandoffValidator.Validate(builder, AgentNames.Tester);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(AgentNames.DataModelBuilder, AgentNames.UIBuilder)]
    [InlineData(AgentNames.UIBuilder, AgentNames.DataPopulation)]
    [InlineData(AgentNames.DataPopulation, AgentNames.DataModelBuilder)]
    public void Builder_CannotHandoffToOtherBuilder(string fromBuilder, string toBuilder)
    {
        var result = HandoffValidator.Validate(fromBuilder, toBuilder);
        Assert.False(result.IsValid);
        Assert.Contains("cannot hand off directly to builder", result.ErrorMessage);
    }

    [Theory]
    [InlineData(AgentNames.BusinessAnalyst)]
    [InlineData(AgentNames.Reviewer)]
    [InlineData(AgentNames.Tester)]
    public void Governance_CanHandoffToSA(string governance)
    {
        var result = HandoffValidator.Validate(governance, AgentNames.SolutionArchitect);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(AgentNames.BusinessAnalyst, AgentNames.DataModelBuilder)]
    [InlineData(AgentNames.Reviewer, AgentNames.UIBuilder)]
    [InlineData(AgentNames.Tester, AgentNames.DataPopulation)]
    public void Governance_CannotHandoffToBuilder(string governance, string builder)
    {
        var result = HandoffValidator.Validate(governance, builder);
        Assert.False(result.IsValid);
        Assert.Contains("can only hand off to Solution Architect", result.ErrorMessage);
    }

    [Fact]
    public void Governance_CannotHandoffToOtherGovernance()
    {
        var result = HandoffValidator.Validate(AgentNames.Reviewer, AgentNames.Tester);
        Assert.False(result.IsValid);
        Assert.Contains("can only hand off to Solution Architect", result.ErrorMessage);
    }

    [Fact]
    public void UnknownAgent_IsAllowed()
    {
        var result = HandoffValidator.Validate("UnknownAgent", AgentNames.SolutionArchitect);
        Assert.True(result.IsValid);
    }
}
