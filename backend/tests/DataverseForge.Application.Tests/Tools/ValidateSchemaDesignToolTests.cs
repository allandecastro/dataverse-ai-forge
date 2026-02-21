using System.Text.Json;
using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class ValidateSchemaDesignToolTests
{
    private readonly ValidateSchemaDesignTool _tool = new();

    private static string MakeSpec(object tables, object? relationships = null)
    {
        var spec = new Dictionary<string, object> { ["tables"] = tables };
        if (relationships != null)
            spec["relationships"] = relationships;
        return JsonSerializer.Serialize(spec);
    }

    [Fact]
    public async Task ValidateAsync_CleanDesign_ReturnsApprovedWith100()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "cr_project",
                description = "Project tracking table",
                isAuditEnabled = true,
                isSearchEnabled = true,
                alternateKeys = new[] { new { name = "cr_projectcode" } },
                fields = new[]
                {
                    new { name = "cr_name", type = "text" },
                    new { name = "cr_status", type = "choice" }
                }
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "all", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(100, result.Score);
        Assert.Equal("APPROVED", result.Verdict);
        Assert.Equal("LOW", result.RiskLevel);
    }

    [Fact]
    public async Task ValidateAsync_MissingPrefix_ReturnsNamingWarning()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "Project",
                description = "A table",
                isAuditEnabled = true,
                isSearchEnabled = true,
                alternateKeys = new[] { new { name = "code" } },
                fields = new[]
                {
                    new { name = "Name", type = "text" }
                }
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "naming", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Rule == "naming" && i.Message.Contains("missing a publisher prefix"));
    }

    [Fact]
    public async Task ValidateAsync_SpacesInName_ReturnsNamingError()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "cr_my table",
                fields = Array.Empty<object>()
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "naming", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Message.Contains("spaces or hyphens"));
    }

    [Fact]
    public async Task ValidateAsync_ReservedWord_ReturnsWarning()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "cr_account",
                fields = Array.Empty<object>()
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "naming", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Rule == "naming" && i.Message.Contains("reserved"));
    }

    [Fact]
    public async Task ValidateAsync_TooManyFields_ReturnsLimitError()
    {
        var fields = Enumerable.Range(0, 1024)
            .Select(i => new { name = $"cr_field{i}", type = "text" })
            .ToArray();

        var spec = MakeSpec(new[] { new { name = "cr_bigtable", fields } });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "limits", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Rule == "limits" && i.Message.Contains("1024"));
    }

    [Fact]
    public async Task ValidateAsync_MissingDescription_ReturnsBestPracticeInfo()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "cr_widget",
                fields = new[] { new { name = "cr_name", type = "text" } }
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "bestPractice", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Rule == "bestPractice" && i.Message.Contains("missing a description"));
    }

    [Fact]
    public async Task ValidateAsync_PiiWithoutFieldSecurity_ReturnsSecurityError()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "cr_employee",
                fields = new[]
                {
                    new { name = "cr_ssn", type = "text", fieldLevelSecurity = false, isPii = false }
                }
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "security", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Rule == "security" && i.Message.Contains("PII"));
    }

    [Fact]
    public async Task ValidateAsync_CascadeDelete_ReturnsRelationshipWarning()
    {
        var spec = MakeSpec(
            new[] { new { name = "cr_parent", fields = Array.Empty<object>() } },
            new[]
            {
                new { parentTable = "cr_parent", childTable = "cr_child", cascadeDelete = "cascade" }
            });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "relationships", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Rule == "relationships" && i.Message.Contains("Cascade delete"));
    }

    [Fact]
    public async Task ValidateAsync_ScoreDeductions_CalculatedCorrectly()
    {
        // 2 errors (-20) + 1 warning (-5) + 1 info (-1) = 74
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "my table",  // error: spaces, error: invalid chars
                fields = new[]
                {
                    new { name = "Name", type = "text" }  // warning: no prefix
                }
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "naming", CancellationToken.None);

        Assert.True(result.Score < 100);
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NamingOnlyFilter_DoesNotCheckBestPractice()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "cr_widget",
                fields = new[] { new { name = "cr_name", type = "text" } }
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "naming", CancellationToken.None);

        Assert.DoesNotContain(result.Issues, i => i.Rule == "bestPractice");
    }

    [Fact]
    public async Task ValidateAsync_AllCategory_RunsEveryRule()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "Widget",  // naming: no prefix
                fields = new[]
                {
                    new { name = "cr_ssn", type = "text", fieldLevelSecurity = false, isPii = false }  // security: PII
                }
                // bestPractice: no description, no audit, no search
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "all", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Rule == "naming");
        Assert.Contains(result.Issues, i => i.Rule == "security");
        Assert.Contains(result.Issues, i => i.Rule == "bestPractice");
    }

    [Fact]
    public async Task ValidateAsync_InvalidJson_ReturnsParseError()
    {
        var result = await _tool.ValidateSchemaDesignAsync("not valid json{{{", "all", CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Rule == "parse");
    }

    [Fact]
    public async Task ValidateAsync_VerdictMapping_NeedsChangesWhenErrors()
    {
        var spec = MakeSpec(new[]
        {
            new
            {
                name = "cr_my-table",  // error: hyphens
                fields = Array.Empty<object>()
            }
        });

        var result = await _tool.ValidateSchemaDesignAsync(spec, "naming", CancellationToken.None);

        Assert.Equal("NEEDS_CHANGES", result.Verdict);
    }
}
