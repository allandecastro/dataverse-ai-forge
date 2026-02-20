using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class ValidateSchemaNameToolTests
{
    private readonly ValidateSchemaNameTool _tool = new();

    [Fact]
    public async Task ValidateSchemaNameAsync_ValidName_ReturnsIsValidTrue()
    {
        // Act
        var result = await _tool.ValidateSchemaNameAsync("customerorder", "cr123", "table", CancellationToken.None);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("cr123_customerorder", result.ValidatedName);
        Assert.Empty(result.Issues);
        Assert.Null(result.Suggestion);
    }

    [Fact]
    public async Task ValidateSchemaNameAsync_NameWithSpaces_CorrectsSuggestion()
    {
        // Act
        var result = await _tool.ValidateSchemaNameAsync("Customer Order", "cr123", "table", CancellationToken.None);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, i => i.Rule == "NoSpaces");
        Assert.Equal("cr123_customerorder", result.Suggestion);
    }

    [Fact]
    public async Task ValidateSchemaNameAsync_NameExceedingMaxLength_TruncatesName()
    {
        // Arrange — create a name that, with prefix, exceeds 128 chars
        var longName = new string('a', 130);

        // Act
        var result = await _tool.ValidateSchemaNameAsync(longName, "cr123", "table", CancellationToken.None);

        // Assert
        Assert.Contains(result.Issues, i => i.Rule == "MaxLength");
        Assert.True(result.ValidatedName.Length <= 128);
    }

    [Fact]
    public async Task ValidateSchemaNameAsync_FieldType_UsesFieldMaxLength()
    {
        // Arrange — create a name that, with prefix, exceeds 64 chars but not 128
        var name = new string('b', 65);

        // Act
        var result = await _tool.ValidateSchemaNameAsync(name, "cr123", "field", CancellationToken.None);

        // Assert
        Assert.Contains(result.Issues, i => i.Rule == "MaxLength");
        Assert.True(result.ValidatedName.Length <= 64);
    }
}
