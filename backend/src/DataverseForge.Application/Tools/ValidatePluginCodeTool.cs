using System.ComponentModel;
using System.Text.RegularExpressions;
using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

public class ValidatePluginCodeTool
{
    [Description("Validate plugin C# code against Dataverse best practices and security rules. "
        + "Checks for common anti-patterns, missing error handling, and forbidden API usage. "
        + "Returns a validation result with score, issues, and recommendations.")]
    public Task<SchemaDesignReview> ValidatePluginCodeAsync(
        [Description("The C# source code of the plugin")] string csharpCode,
        [Description("The plugin name for reporting")] string pluginName,
        CancellationToken cancellationToken)
    {
        var issues = new List<SchemaDesignIssue>();
        var recommendations = new List<string>();

        // Check for IPlugin implementation
        if (!csharpCode.Contains("IPlugin"))
            issues.Add(new SchemaDesignIssue("error", "implementation", "Plugin does not implement IPlugin interface.", "Add ': IPlugin' to the class declaration."));

        // Check for Execute method
        if (!csharpCode.Contains("Execute(IServiceProvider"))
            issues.Add(new SchemaDesignIssue("error", "implementation", "Missing Execute(IServiceProvider) method.", "Implement the Execute method as required by IPlugin."));

        // Check for tracing service
        if (!csharpCode.Contains("ITracingService"))
            issues.Add(new SchemaDesignIssue("warning", "logging", "ITracingService not used.", "Always use ITracingService for logging in plugins."));

        // Check for error handling
        if (!csharpCode.Contains("try") || !csharpCode.Contains("catch"))
            issues.Add(new SchemaDesignIssue("warning", "errorHandling", "Missing try-catch block.", "Wrap logic in try-catch with InvalidPluginExecutionException."));

        // Check for InvalidPluginExecutionException
        if (!csharpCode.Contains("InvalidPluginExecutionException"))
            issues.Add(new SchemaDesignIssue("warning", "errorHandling", "InvalidPluginExecutionException not used.", "Use InvalidPluginExecutionException for business errors."));

        // Check for forbidden patterns
        if (Regex.IsMatch(csharpCode, @"\basync\b.*\bTask\b"))
            issues.Add(new SchemaDesignIssue("error", "async", "Async/await detected in plugin code.", "NEVER use async/await in synchronous plugins. Use synchronous SDK calls only."));

        if (csharpCode.Contains("HttpClient"))
            issues.Add(new SchemaDesignIssue("error", "externalCall", "HttpClient usage detected.", "NEVER use HttpClient in synchronous plugins. Use webhooks or async plugins for external calls."));

        if (csharpCode.Contains("Thread.Sleep"))
            issues.Add(new SchemaDesignIssue("error", "threading", "Thread.Sleep detected.", "NEVER use Thread.Sleep in plugins. It blocks the execution pipeline."));

        // Check for exception swallowing
        if (Regex.IsMatch(csharpCode, @"catch\s*\([^)]*\)\s*\{[\s\r\n]*\}"))
            issues.Add(new SchemaDesignIssue("error", "errorHandling", "Empty catch block detected (swallowing exceptions).", "NEVER swallow exceptions. Log and re-throw or wrap in InvalidPluginExecutionException."));

        // Check for InputParameters validation
        if (!csharpCode.Contains("InputParameters"))
            issues.Add(new SchemaDesignIssue("warning", "validation", "InputParameters not checked.", "Always validate InputParameters before accessing them."));

        // Check for context extraction
        if (!csharpCode.Contains("IPluginExecutionContext"))
            issues.Add(new SchemaDesignIssue("warning", "context", "IPluginExecutionContext not extracted.", "Always extract the plugin execution context."));

        // Recommendations
        if (!csharpCode.Contains("LogicalName"))
            recommendations.Add("Consider validating the target entity's LogicalName to prevent execution on wrong entities.");

        if (!csharpCode.Contains("Depth"))
            recommendations.Add("Consider checking context.Depth to prevent infinite loops from recursive plugin triggers.");

        // Calculate score
        var score = 100;
        foreach (var issue in issues)
        {
            score -= issue.Severity switch
            {
                "error" => 15,
                "warning" => 5,
                _ => 1
            };
        }
        score = Math.Max(0, Math.Min(100, score));

        var verdict = score switch
        {
            >= 90 => "APPROVED",
            >= 70 => "APPROVED_WITH_WARNINGS",
            >= 40 => "NEEDS_CHANGES",
            _ => "REJECTED"
        };

        var riskLevel = score switch
        {
            >= 90 => "green",
            >= 70 => "yellow",
            _ => "orange"
        };

        return Task.FromResult(new SchemaDesignReview(
            IsValid: score >= 70,
            Score: score,
            Verdict: verdict,
            RiskLevel: riskLevel,
            Issues: issues.AsReadOnly(),
            Recommendations: recommendations.AsReadOnly()));
    }
}
