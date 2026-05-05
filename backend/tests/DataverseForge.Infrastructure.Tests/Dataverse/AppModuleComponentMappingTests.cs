using System.Text.Json;
using DataverseForge.Infrastructure.Dataverse;

namespace DataverseForge.Infrastructure.Tests.Dataverse;

/// <summary>
/// Exercises the heterogeneous-response branching in
/// <c>DataverseClient.MapAppComponent</c>. The Web API <c>RetrieveAppComponents</c>
/// action returns entities, forms, savedqueries, charts, dashboards, sitemaps —
/// all in one collection — and the agent needs a uniform shape on top.
/// </summary>
public class AppModuleComponentMappingTests
{
    private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void MapAppComponent_Entity_PullsMetadataIdAndLogicalName()
    {
        var metadataId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.EntityMetadata",
          "MetadataId": "{{metadataId}}",
          "LogicalName": "account",
          "DisplayName": {
            "UserLocalizedLabel": { "Label": "Account" }
          }
        }
        """);

        var (kind, id, name, logicalName) = DataverseClient.MapAppComponent(element);

        Assert.Equal("entity", kind);
        Assert.Equal(metadataId, id);
        Assert.Equal("Account", name);
        Assert.Equal("account", logicalName);
    }

    [Fact]
    public void MapAppComponent_SystemForm_PullsFormIdAndName()
    {
        var formId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.systemform",
          "formid": "{{formId}}",
          "name": "Account Main Form",
          "objecttypecode": "account"
        }
        """);

        var (kind, id, name, logicalName) = DataverseClient.MapAppComponent(element);

        Assert.Equal("systemform", kind);
        Assert.Equal(formId, id);
        Assert.Equal("Account Main Form", name);
        Assert.Equal("account", logicalName);
    }

    [Fact]
    public void MapAppComponent_SavedQuery_PullsSavedQueryId()
    {
        var savedQueryId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.savedquery",
          "savedqueryid": "{{savedQueryId}}",
          "name": "Active Accounts",
          "returnedtypecode": "account"
        }
        """);

        var (kind, id, name, logicalName) = DataverseClient.MapAppComponent(element);

        Assert.Equal("savedquery", kind);
        Assert.Equal(savedQueryId, id);
        Assert.Equal("Active Accounts", name);
        Assert.Equal("account", logicalName);
    }

    [Fact]
    public void MapAppComponent_Chart_PullsVisualizationId()
    {
        var chartId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.savedqueryvisualization",
          "savedqueryvisualizationid": "{{chartId}}",
          "name": "Revenue by Owner",
          "primaryentitytypecode": "opportunity"
        }
        """);

        var (kind, id, name, logicalName) = DataverseClient.MapAppComponent(element);

        Assert.Equal("savedqueryvisualization", kind);
        Assert.Equal(chartId, id);
        Assert.Equal("Revenue by Owner", name);
        Assert.Equal("opportunity", logicalName);
    }

    [Fact]
    public void MapAppComponent_SiteMap_PrefersUniqueName()
    {
        var sitemapId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.sitemap",
          "sitemapid": "{{sitemapId}}",
          "sitemapname": "Sales",
          "sitemapnameunique": "msdyn_SalesSitemap"
        }
        """);

        var (kind, id, name, logicalName) = DataverseClient.MapAppComponent(element);

        Assert.Equal("sitemap", kind);
        Assert.Equal(sitemapId, id);
        Assert.Equal("msdyn_SalesSitemap", name);
        Assert.Null(logicalName);
    }

    [Fact]
    public void MapAppComponent_UnknownType_FallsThroughWithBareTypeName()
    {
        var someId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.WeirdNewThing",
          "weirdnewthingid": "{{someId}}",
          "name": "X"
        }
        """);

        var (kind, id, _, _) = DataverseClient.MapAppComponent(element);

        // Unknown but typed: keep the bare type so the agent can surface it.
        Assert.Equal("weirdnewthing", kind);
        // Falls back to "any property ending in 'id'" to extract the GUID.
        Assert.Equal(someId, id);
    }

    [Theory]
    // Source: Microsoft Learn — AppModuleComponent componenttype Choices/Options.
    // appmodulecomponent uses a SCOPED enum that is DIFFERENT from
    // SolutionComponentType — e.g. 29 means BPF here, not workflow.
    [InlineData(1, "entity")]
    [InlineData(26, "savedquery")]
    [InlineData(29, "businessprocessflow")]
    [InlineData(48, "ribboncommand")]
    [InlineData(59, "savedqueryvisualization")]
    [InlineData(60, "systemform")]
    [InlineData(62, "sitemap")]
    public void MapAppComponent_AppModuleComponent_PullsObjectIdAndMapsComponentType(int componentType, string expectedKind)
    {
        // RetrieveAppComponents on current Dataverse returns appmodulecomponent
        // join-table rows — must be unwrapped to the real objectid + componenttype.
        var objectId = Guid.NewGuid();
        var rowId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.appmodulecomponent",
          "appmodulecomponentid": "{{rowId}}",
          "objectid": "{{objectId}}",
          "componenttype": {{componentType}}
        }
        """);

        var (kind, id, _, _) = DataverseClient.MapAppComponent(element);

        Assert.Equal(expectedKind, kind);
        Assert.Equal(objectId, id);
    }

    [Fact]
    public void MapAppComponent_AppModuleComponent_UnknownComponentType_FallsBackToTypedString()
    {
        var objectId = Guid.NewGuid();
        var element = Element($$"""
        {
          "@odata.type": "#Microsoft.Dynamics.CRM.appmodulecomponent",
          "appmodulecomponentid": "{{Guid.NewGuid()}}",
          "objectid": "{{objectId}}",
          "componenttype": 99999
        }
        """);

        var (kind, id, _, _) = DataverseClient.MapAppComponent(element);

        Assert.Equal("componenttype-99999", kind);
        Assert.Equal(objectId, id);
    }

    [Fact]
    public void MapAppComponent_MissingTypeAndId_DropsThroughCleanly()
    {
        // No @odata.type, no id — the caller will see Guid.Empty and skip the row.
        var element = Element("""{ "name": "no id, no type" }""");

        var (kind, id, _, _) = DataverseClient.MapAppComponent(element);

        Assert.Equal("unknown", kind);
        Assert.Equal(Guid.Empty, id);
    }
}
