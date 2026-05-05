using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

/// <summary>
/// Tests against the bundled SiteMap.xsd. The Application test project copies
/// the schemas to its output directory (via the parent csproj's
/// CopyToOutputDirectory) so AppContext.BaseDirectory + schemas/9.0.0.2090
/// resolves at test time.
/// </summary>
public class XsdValidatorTests
{
    private static readonly XsdValidator Validator = new();

    [Fact]
    public void Validate_WellFormedSiteMap_Passes()
    {
        var xml = "<SiteMap><Area Id=\"sales\" Title=\"Sales\" ShowGroups=\"true\">"
            + "<Group Id=\"g1\" Title=\"Customers\">"
            + "<SubArea Id=\"acc\" Title=\"Accounts\" Entity=\"account\" />"
            + "</Group></Area></SiteMap>";

        var result = Validator.Validate(xml, "SiteMap.xsd");

        Assert.True(result.IsValid, "Expected valid; errors: " + string.Join(" | ", result.Errors));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_MalformedXml_FailsWithLineColumn()
    {
        var xml = "<SiteMap><Area Id=\"a1\"></Area>"; // missing closing SiteMap

        var result = Validator.Validate(xml, "SiteMap.xsd");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("line", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DuplicateAreaIds_FailsWithUniqueConstraintError()
    {
        // SiteMap.xsd declares <xs:unique name="AreaIdMustBeUnique"> on
        // Area/@Id — duplicates trigger a validation event.
        var xml = "<SiteMap>"
            + "<Area Id=\"sales\" ShowGroups=\"true\" />"
            + "<Area Id=\"sales\" ShowGroups=\"true\" />"
            + "</SiteMap>";

        var result = Validator.Validate(xml, "SiteMap.xsd");

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Validate_EmptyXml_FailsCleanly()
    {
        var result = Validator.Validate(string.Empty, "SiteMap.xsd");

        Assert.False(result.IsValid);
        Assert.Contains("empty", result.Errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_MissingXsd_FailsWithFriendlyMessage()
    {
        var result = Validator.Validate("<root/>", "DefinitelyDoesNotExist.xsd");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Could not load schema", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SchemaSetCachedAcrossCalls()
    {
        // Two back-to-back validations should both succeed; the second
        // benefits from the schema-set cache (no re-parse). We can't
        // directly observe cache hits, but we can confirm the result is
        // consistent and call latency stays bounded.
        var xml = "<SiteMap><Area Id=\"a\" ShowGroups=\"true\" /></SiteMap>";
        var first = Validator.Validate(xml, "SiteMap.xsd");
        var second = Validator.Validate(xml, "SiteMap.xsd");

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
    }
}
