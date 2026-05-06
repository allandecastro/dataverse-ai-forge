using DataverseForge.Infrastructure.Dataverse;

namespace DataverseForge.Infrastructure.Tests.Dataverse;

/// <summary>
/// Exercises <c>DataverseClient.ParseDescriptorComponents</c> — the helper that
/// reads the <c>descriptor</c> JSON column on an appmodule for component rows.
/// Real session 2026-05-06: the maker-portal "Test" app had its 3 entities only
/// in the descriptor; the appmodulecomponent join table was empty. So the
/// parser is the load-bearing source for that flow.
/// </summary>
public class AppDescriptorParserTests
{
    [Fact]
    public void ParseDescriptor_RealMakerAppShape_ReturnsAllEntitiesWithLogicalNames()
    {
        // Verbatim shape from the 2026-05-06 trace, "Test" app.
        var descriptor = """
        {
          "appInfo": {
            "AppId": "35b71932-aa48-f111-bec6-7c1e52608b99",
            "Title": "Test",
            "UniqueName": "eppc_Test",
            "Components": [
              {"Id": "70816501-edb9-4740-a16c-6a5efbc05d84", "Type": 1},
              {"Id": "608861bc-50a4-4c5f-a02c-21fe1943e2cf", "Type": 1},
              {"Id": "789cd48f-3946-f111-bec6-7c1e52608b99", "Type": 1},
              {"Id": "225228dd-8748-f111-bec6-7c1e52608f47", "Type": 60},
              {"Id": "90c89bd6-7148-f111-bec6-7c1e52608f47", "Type": 60},
              {"Id": "2c40132e-aa48-f111-bec6-7c1e52608c2d", "Type": 62}
            ],
            "AppComponents": {
              "Entities": [
                {"Id": "70816501-edb9-4740-a16c-6a5efbc05d84", "LogicalName": "account"},
                {"Id": "608861bc-50a4-4c5f-a02c-21fe1943e2cf", "LogicalName": "contact"},
                {"Id": "789cd48f-3946-f111-bec6-7c1e52608b99", "LogicalName": "eppc_gift"}
              ]
            }
          }
        }
        """;

        var rows = DataverseClient.ParseDescriptorComponents(descriptor);

        // 3 entities + 2 forms + 1 sitemap = 6.
        Assert.Equal(6, rows.Count);

        // Each entity has its LogicalName attached from AppComponents.Entities.
        var account = rows.Single(r => r.LogicalName == "account");
        Assert.Equal(1, account.Type);
        Assert.Equal(Guid.Parse("70816501-edb9-4740-a16c-6a5efbc05d84"), account.Id);

        Assert.Contains(rows, r => r.LogicalName == "contact" && r.Type == 1);
        Assert.Contains(rows, r => r.LogicalName == "eppc_gift" && r.Type == 1);

        // Forms and sitemap come through with their type but no LogicalName
        // (descriptor doesn't carry one for those).
        Assert.Equal(2, rows.Count(r => r.Type == 60));
        Assert.Single(rows, r => r.Type == 62);
        Assert.All(rows.Where(r => r.Type != 1), r => Assert.Null(r.LogicalName));
    }

    [Fact]
    public void ParseDescriptor_EntitiesOnlyInAppComponents_StillSurfacesAsType1()
    {
        // Some descriptor variants emit entities only under
        // AppComponents.Entities (without a corresponding Components[] entry).
        // The parser must still emit those as type=1 rows so the agent's
        // "what's attached?" answer doesn't drop them.
        var descriptor = """
        {
          "appInfo": {
            "Components": [],
            "AppComponents": {
              "Entities": [
                {"Id": "11111111-1111-1111-1111-111111111111", "LogicalName": "account"}
              ]
            }
          }
        }
        """;

        var rows = DataverseClient.ParseDescriptorComponents(descriptor);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].Type);
        Assert.Equal("account", rows[0].LogicalName);
    }

    [Fact]
    public void ParseDescriptor_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Empty(DataverseClient.ParseDescriptorComponents(null));
        Assert.Empty(DataverseClient.ParseDescriptorComponents(""));
        Assert.Empty(DataverseClient.ParseDescriptorComponents("   "));
    }

    [Fact]
    public void ParseDescriptor_MalformedJson_ReturnsEmpty()
    {
        // Malformed input must not throw — the GetAppComponents read path
        // depends on this falling through to the join-table source rather
        // than failing the whole composite read.
        Assert.Empty(DataverseClient.ParseDescriptorComponents("{ broken"));
        Assert.Empty(DataverseClient.ParseDescriptorComponents("[]"));        // wrong root shape
        Assert.Empty(DataverseClient.ParseDescriptorComponents("\"string\"")); // wrong root shape
    }

    [Fact]
    public void ParseDescriptor_MissingAppInfoOrComponents_ReturnsEmpty()
    {
        // Real appmodule rows can have descriptor=null OR descriptor="{}"
        // before publish. Both should degrade silently to empty.
        Assert.Empty(DataverseClient.ParseDescriptorComponents("{}"));
        Assert.Empty(DataverseClient.ParseDescriptorComponents("""{"appInfo": {}}"""));
    }
}
