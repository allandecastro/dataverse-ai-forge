using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class RequestApprovalToolTests
{
    private readonly RequestApprovalTool _tool = new();

    [Fact]
    public async Task RequestApprovalAsync_ReturnsConfirmationMessage()
    {
        var result = await _tool.RequestApprovalAsync(
            "CreateTable",
            "Create the Feedback table with 8 fields",
            "orange",
            "{\"tableName\":\"ctso_feedback\"}",
            CancellationToken.None);

        Assert.Contains("Approval requested for CreateTable", result);
        Assert.Contains("Create the Feedback table with 8 fields", result);
        Assert.Contains("Risk level: orange", result);
        Assert.Contains("Waiting for user decision", result);
    }

    [Fact]
    public async Task RequestApprovalAsync_IncludesAllRiskLevels()
    {
        foreach (var risk in new[] { "green", "yellow", "orange", "red" })
        {
            var result = await _tool.RequestApprovalAsync(
                "TestOp", "desc", risk, "{}", CancellationToken.None);

            Assert.Contains($"Risk level: {risk}", result);
        }
    }

    [Fact]
    public async Task RequestApprovalAsync_InstructsAgentToWait()
    {
        var result = await _tool.RequestApprovalAsync(
            "DeleteTable", "Delete everything", "red", "{}", CancellationToken.None);

        Assert.Contains("Do NOT proceed until the user responds", result);
    }
}
