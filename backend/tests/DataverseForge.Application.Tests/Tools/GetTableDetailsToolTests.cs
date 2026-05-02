using DataverseForge.Application.Tools;
using DataverseForge.Domain.Interfaces;
using DataverseForge.Domain.Models;
using Moq;

namespace DataverseForge.Application.Tests.Tools;

public class GetTableDetailsToolTests
{
    [Fact]
    public async Task GetTableDetailsAsync_DelegatesToDataverseClientWithCorrectLogicalName()
    {
        // Arrange
        var expected = new TableDetails(
            LogicalName: "account",
            DisplayName: "Account",
            SchemaName: "Account",
            IsCustom: false,
            IsManaged: false,
            OwnershipType: "UserOwned",
            PrimaryIdAttribute: "accountid",
            PrimaryNameAttribute: "name",
            Columns: new List<ColumnInfo>
            {
                new("name", "Account Name", "StringType", false, "ApplicationRequired", 160, true)
            }.AsReadOnly(),
            Relationships: new List<RelationshipInfo>
            {
                new("account_contacts", "OneToMany", "account", "accountid", "contact", "parentcustomerid")
            }.AsReadOnly(),
            AlternateKeys: new List<AlternateKeyInfo>().AsReadOnly());

        var mockClient = new Mock<IDataverseClient>();
        mockClient
            .Setup(c => c.GetTableDetailsAsync("account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tool = new GetTableDetailsTool(mockClient.Object);

        // Act
        var result = await tool.GetTableDetailsAsync("account", CancellationToken.None);

        // Assert
        Assert.Equal(expected, result);
        mockClient.Verify(c => c.GetTableDetailsAsync("account", It.IsAny<CancellationToken>()), Times.Once);
    }
}
