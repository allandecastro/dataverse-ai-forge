using DataverseForge.Application.Tools;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tests.Tools;

public class SiteMapXmlBuilderTests
{
    private static SiteMapLayoutSpec MakeSpec(params SiteMapAreaSpec[] areas) =>
        new(areas);

    [Fact]
    public void Validate_EmptySpec_ReportsError()
    {
        var spec = new SiteMapLayoutSpec(Array.Empty<SiteMapAreaSpec>());

        var errors = SiteMapXmlBuilder.Validate(spec);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("no Areas", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AreaWithoutId_ReportsError()
    {
        var spec = MakeSpec(new SiteMapAreaSpec(
            Id: "", Title: "X", Icon: null, ShowGroups: false,
            Groups: Array.Empty<SiteMapGroupSpec>()));

        var errors = SiteMapXmlBuilder.Validate(spec);

        Assert.Contains(errors, e => e.Contains("areas[0].id is required"));
    }

    [Fact]
    public void Validate_DuplicateAreaIds_ReportsError()
    {
        var spec = MakeSpec(
            new SiteMapAreaSpec("sales", "Sales", null, false, Array.Empty<SiteMapGroupSpec>()),
            new SiteMapAreaSpec("sales", "Other", null, false, Array.Empty<SiteMapGroupSpec>()));

        var errors = SiteMapXmlBuilder.Validate(spec);

        Assert.Contains(errors, e => e.Contains("'sales' is duplicated"));
    }

    [Fact]
    public void Validate_DuplicateGroupIdsWithinArea_ReportsError()
    {
        var spec = MakeSpec(new SiteMapAreaSpec(
            "a1", "A1", null, true,
            new[]
            {
                new SiteMapGroupSpec("g1", "G1", Array.Empty<SiteMapSubAreaSpec>()),
                new SiteMapGroupSpec("g1", "G2", Array.Empty<SiteMapSubAreaSpec>()),
            }));

        var errors = SiteMapXmlBuilder.Validate(spec);

        Assert.Contains(errors, e => e.Contains("groups[1].id 'g1' is duplicated"));
    }

    [Fact]
    public void Validate_SubAreaWithBothEntityAndUrl_ReportsError()
    {
        var sub = new SiteMapSubAreaSpec(
            Id: "s1", Title: "Sub", Entity: "account", Url: "https://example.com",
            Type: null, PageId: null, Icon: null,
            Privileges: Array.Empty<SiteMapPrivilegeSpec>());
        var group = new SiteMapGroupSpec("g1", "G", new[] { sub });
        var spec = MakeSpec(new SiteMapAreaSpec("a1", "A", null, true, new[] { group }));

        var errors = SiteMapXmlBuilder.Validate(spec);

        Assert.Contains(errors, e => e.Contains("more than one of entity / url / pageId"));
    }

    [Fact]
    public void Validate_GroupsWithoutShowGroups_ReportsError()
    {
        // Real session likely failure: agent ships groups but forgets
        // ShowGroups=true. Groups exist but don't render.
        var sub = new SiteMapSubAreaSpec("s1", "S", "account", null, null, null, null,
            Array.Empty<SiteMapPrivilegeSpec>());
        var group = new SiteMapGroupSpec("g1", "G", new[] { sub });
        var spec = MakeSpec(new SiteMapAreaSpec("a1", "A", null, ShowGroups: false, new[] { group }));

        var errors = SiteMapXmlBuilder.Validate(spec);

        Assert.Contains(errors, e => e.Contains("showGroups=false") && e.Contains("set showGroups=true"));
    }

    [Fact]
    public void Render_SimpleArea_ProducesValidXml()
    {
        var sub = new SiteMapSubAreaSpec("acc", "Accounts", "account", null, null, null, null,
            Array.Empty<SiteMapPrivilegeSpec>());
        var group = new SiteMapGroupSpec("g1", "Customers", new[] { sub });
        var spec = MakeSpec(new SiteMapAreaSpec("sales", "Sales", null, ShowGroups: true, new[] { group }));

        var xml = SiteMapXmlBuilder.Render(spec);

        Assert.Contains("<SiteMap>", xml, StringComparison.Ordinal);
        Assert.Contains("<Area Id=\"sales\"", xml, StringComparison.Ordinal);
        Assert.Contains("Title=\"Sales\"", xml, StringComparison.Ordinal);
        Assert.Contains("ShowGroups=\"true\"", xml, StringComparison.Ordinal);
        Assert.Contains("<Group Id=\"g1\"", xml, StringComparison.Ordinal);
        Assert.Contains("<SubArea Id=\"acc\"", xml, StringComparison.Ordinal);
        Assert.Contains("Entity=\"account\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SubAreaWithUrl_EmitsUrlAttribute()
    {
        var sub = new SiteMapSubAreaSpec("ext", "External", null, "https://contoso.com", null, null, null,
            Array.Empty<SiteMapPrivilegeSpec>());
        var group = new SiteMapGroupSpec("g1", "G", new[] { sub });
        var spec = MakeSpec(new SiteMapAreaSpec("a1", "A", null, true, new[] { group }));

        var xml = SiteMapXmlBuilder.Render(spec);

        Assert.Contains("Url=\"https://contoso.com\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Entity=\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_PrivilegesEmittedAsChildren()
    {
        var priv = new SiteMapPrivilegeSpec("account", "Read");
        var sub = new SiteMapSubAreaSpec("acc", "Accounts", "account", null, null, null, null,
            new[] { priv });
        var group = new SiteMapGroupSpec("g1", "G", new[] { sub });
        var spec = MakeSpec(new SiteMapAreaSpec("a1", "A", null, true, new[] { group }));

        var xml = SiteMapXmlBuilder.Render(spec);

        Assert.Contains("<Privilege Entity=\"account\" Privilege=\"Read\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MultiAreaSpec_PreservesOrder()
    {
        var spec = MakeSpec(
            new SiteMapAreaSpec("sales", "Sales", null, true, new[]
            {
                new SiteMapGroupSpec("g_sales", "Customers", new[]
                {
                    new SiteMapSubAreaSpec("acc_sub", "Accounts", "account", null, null, null, null, Array.Empty<SiteMapPrivilegeSpec>()),
                }),
            }),
            new SiteMapAreaSpec("service", "Service", null, true, new[]
            {
                new SiteMapGroupSpec("g_svc", "Tickets", new[]
                {
                    new SiteMapSubAreaSpec("inc_sub", "Incidents", "incident", null, null, null, null, Array.Empty<SiteMapPrivilegeSpec>()),
                }),
            }));

        var xml = SiteMapXmlBuilder.Render(spec);

        var salesIdx = xml.IndexOf("Id=\"sales\"", StringComparison.Ordinal);
        var serviceIdx = xml.IndexOf("Id=\"service\"", StringComparison.Ordinal);
        Assert.True(salesIdx >= 0 && serviceIdx > salesIdx, "Areas should appear in spec order");
    }

    [Fact]
    public void RenderedXml_PassesXsdValidation()
    {
        // End-to-end check: render a realistic spec and run it through the
        // bundled SiteMap.xsd. If they ever go out of sync, this test fails.
        var spec = MakeSpec(new SiteMapAreaSpec(
            "demo", "Demo", null, true,
            new[]
            {
                new SiteMapGroupSpec("customer", "Customer", new[]
                {
                    new SiteMapSubAreaSpec("acc", "Accounts", "account", null, null, null, null, Array.Empty<SiteMapPrivilegeSpec>()),
                    new SiteMapSubAreaSpec("con", "Contacts", "contact", null, null, null, null, Array.Empty<SiteMapPrivilegeSpec>()),
                }),
                new SiteMapGroupSpec("item", "Item", new[]
                {
                    new SiteMapSubAreaSpec("gift", "Gifts", "eppc_gift", null, null, null, null, Array.Empty<SiteMapPrivilegeSpec>()),
                }),
            }));

        var xml = SiteMapXmlBuilder.Render(spec);
        var validator = new XsdValidator();
        var result = validator.Validate(xml, "SiteMap.xsd");

        Assert.True(result.IsValid, "XSD errors: " + string.Join(" | ", result.Errors));
    }
}
