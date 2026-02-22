using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class PlanCreateCustomApiToolTests
{
    private readonly PlanCreateCustomApiTool _tool = new();

    [Fact]
    public async Task PlanCreateCustomApi_ValidGlobalApi_ReturnsPlan()
    {
        var plan = await _tool.PlanCreateCustomApiAsync(
            uniqueName: "cr123_CalculateTotal",
            displayName: "Calculate Total",
            description: "Calculates the total for an order",
            bindingType: "Global",
            boundEntityLogicalName: null,
            isFunction: false,
            isPrivate: false,
            requestParametersDefinition: "OrderId:Order ID:String:optional",
            responsePropertiesDefinition: "Total:Total Amount:Decimal",
            CancellationToken.None);

        Assert.NotNull(plan);
        Assert.Equal("cr123_CalculateTotal", plan.UniqueName);
        Assert.Equal("Calculate Total", plan.DisplayName);
        Assert.Equal("Calculates the total for an order", plan.Description);
        Assert.Equal("Global", plan.BindingType);
        Assert.Null(plan.BoundEntityLogicalName);
        Assert.False(plan.IsFunction);
        Assert.False(plan.IsPrivate);
        Assert.Null(plan.PluginCode);
        Assert.NotNull(plan.WebApiPayloadJson);
        Assert.NotEmpty(plan.WebApiPayloadJson);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PlanCreateCustomApi_InvalidBindingType_Warning()
    {
        var plan = await _tool.PlanCreateCustomApiAsync(
            uniqueName: "cr123_BadBinding",
            displayName: "Bad Binding",
            description: "Has invalid binding type",
            bindingType: "InvalidType",
            boundEntityLogicalName: null,
            isFunction: false,
            isPrivate: false,
            requestParametersDefinition: "",
            responsePropertiesDefinition: "",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("InvalidType") && w.Contains("not valid"));
    }

    [Fact]
    public async Task PlanCreateCustomApi_EntityBindingWithoutEntity_Warning()
    {
        var plan = await _tool.PlanCreateCustomApiAsync(
            uniqueName: "cr123_EntityApi",
            displayName: "Entity API",
            description: "Entity binding but no entity specified",
            bindingType: "Entity",
            boundEntityLogicalName: null,
            isFunction: false,
            isPrivate: false,
            requestParametersDefinition: "",
            responsePropertiesDefinition: "",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("bound entity logical name"));
    }

    [Fact]
    public async Task PlanCreateCustomApi_FunctionWarnsAboutSideEffects()
    {
        var plan = await _tool.PlanCreateCustomApiAsync(
            uniqueName: "cr123_GetData",
            displayName: "Get Data",
            description: "Retrieves data",
            bindingType: "Global",
            boundEntityLogicalName: null,
            isFunction: true,
            isPrivate: false,
            requestParametersDefinition: "",
            responsePropertiesDefinition: "",
            CancellationToken.None);

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains(plan.Warnings, w => w.Contains("side effects"));
    }

    [Fact]
    public async Task PlanCreateCustomApi_ParsesRequestParameters()
    {
        var plan = await _tool.PlanCreateCustomApiAsync(
            uniqueName: "cr123_MultiParam",
            displayName: "Multi Param",
            description: "API with multiple request parameters",
            bindingType: "Global",
            boundEntityLogicalName: null,
            isFunction: false,
            isPrivate: false,
            requestParametersDefinition: "OrderId:Order ID:String:optional;Amount:Amount:Decimal",
            responsePropertiesDefinition: "",
            CancellationToken.None);

        Assert.Equal(2, plan.RequestParameters.Count);

        Assert.Equal("OrderId", plan.RequestParameters[0].UniqueName);
        Assert.Equal("Order ID", plan.RequestParameters[0].DisplayName);
        Assert.Equal("String", plan.RequestParameters[0].Type);
        Assert.True(plan.RequestParameters[0].IsOptional);

        Assert.Equal("Amount", plan.RequestParameters[1].UniqueName);
        Assert.Equal("Amount", plan.RequestParameters[1].DisplayName);
        Assert.Equal("Decimal", plan.RequestParameters[1].Type);
        Assert.False(plan.RequestParameters[1].IsOptional);
    }

    [Fact]
    public async Task PlanCreateCustomApi_ParsesResponseProperties()
    {
        var plan = await _tool.PlanCreateCustomApiAsync(
            uniqueName: "cr123_WithResponse",
            displayName: "With Response",
            description: "API with response properties",
            bindingType: "Global",
            boundEntityLogicalName: null,
            isFunction: false,
            isPrivate: false,
            requestParametersDefinition: "",
            responsePropertiesDefinition: "Total:Total Amount:Decimal;Status:Status Code:Integer",
            CancellationToken.None);

        Assert.Equal(2, plan.ResponseProperties.Count);

        Assert.Equal("Total", plan.ResponseProperties[0].UniqueName);
        Assert.Equal("Total Amount", plan.ResponseProperties[0].DisplayName);
        Assert.Equal("Decimal", plan.ResponseProperties[0].Type);

        Assert.Equal("Status", plan.ResponseProperties[1].UniqueName);
        Assert.Equal("Status Code", plan.ResponseProperties[1].DisplayName);
        Assert.Equal("Integer", plan.ResponseProperties[1].Type);
    }

    [Fact]
    public async Task PlanCreateCustomApi_RiskLevelIsOrange()
    {
        var plan = await _tool.PlanCreateCustomApiAsync(
            uniqueName: "cr123_RiskCheck",
            displayName: "Risk Check",
            description: "Check risk level",
            bindingType: "Global",
            boundEntityLogicalName: null,
            isFunction: false,
            isPrivate: false,
            requestParametersDefinition: "",
            responsePropertiesDefinition: "",
            CancellationToken.None);

        Assert.Equal("orange", plan.RiskLevel);
    }
}
