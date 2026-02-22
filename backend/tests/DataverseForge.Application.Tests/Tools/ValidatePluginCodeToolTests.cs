using DataverseForge.Application.Tools;

namespace DataverseForge.Application.Tests.Tools;

public class ValidatePluginCodeToolTests
{
    private readonly ValidatePluginCodeTool _tool = new();

    [Fact]
    public async Task ValidatePluginCode_ValidCode_Approved()
    {
        var validCode = @"
public class MyPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        try
        {
            if (context.InputParameters.Contains(""Target""))
            {
                tracingService.Trace(""Processing..."");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException(""Error"", ex);
        }
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(validCode, "MyPlugin", CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.True(result.Score >= 90);
        Assert.Equal("APPROVED", result.Verdict);
        Assert.Equal("green", result.RiskLevel);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task ValidatePluginCode_MissingIPlugin_Error()
    {
        // Code that does NOT contain "IPlugin" anywhere (no IPlugin interface, no IPluginExecutionContext)
        var code = @"
public class MyHandler
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        try
        {
            if (context != null)
            {
                tracingService.Trace(""Processing..."");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException(""Error"", ex);
        }
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(code, "MyHandler", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Rule == "implementation" && i.Message.Contains("IPlugin"));
    }

    [Fact]
    public async Task ValidatePluginCode_AsyncCode_Error()
    {
        var code = @"
public class MyPlugin : IPlugin
{
    public async Task<Entity> Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        try
        {
            if (context.InputParameters.Contains(""Target""))
            {
                tracingService.Trace(""Processing..."");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException(""Error"", ex);
        }
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(code, "MyPlugin", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Rule == "async" && i.Message.Contains("Async"));
    }

    [Fact]
    public async Task ValidatePluginCode_HttpClient_Error()
    {
        var code = @"
public class MyPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        var client = new HttpClient();
        try
        {
            if (context.InputParameters.Contains(""Target""))
            {
                tracingService.Trace(""Processing..."");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException(""Error"", ex);
        }
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(code, "MyPlugin", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Rule == "externalCall" && i.Message.Contains("HttpClient"));
    }

    [Fact]
    public async Task ValidatePluginCode_ThreadSleep_Error()
    {
        var code = @"
public class MyPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        try
        {
            Thread.Sleep(1000);
            if (context.InputParameters.Contains(""Target""))
            {
                tracingService.Trace(""Processing..."");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException(""Error"", ex);
        }
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(code, "MyPlugin", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Rule == "threading" && i.Message.Contains("Thread.Sleep"));
    }

    [Fact]
    public async Task ValidatePluginCode_EmptyCatch_Error()
    {
        var code = @"
public class MyPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        try
        {
            if (context.InputParameters.Contains(""Target""))
            {
                tracingService.Trace(""Processing..."");
            }
        }
        catch (Exception) {  }
        throw new InvalidPluginExecutionException(""Error"");
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(code, "MyPlugin", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Severity == "error" && i.Rule == "errorHandling" && i.Message.Contains("Empty catch"));
    }

    [Fact]
    public async Task ValidatePluginCode_MissingTracing_Warning()
    {
        var code = @"
public class MyPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        try
        {
            if (context.InputParameters.Contains(""Target""))
            {
                // no tracing
            }
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException(""Error"", ex);
        }
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(code, "MyPlugin", CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Severity == "warning" && i.Rule == "logging" && i.Message.Contains("ITracingService"));
    }

    [Fact]
    public async Task ValidatePluginCode_ManyIssues_Rejected()
    {
        // Missing IPlugin (error), missing Execute(IServiceProvider (error), async Task (error),
        // HttpClient (error), Thread.Sleep (error), missing tracing (warning),
        // missing try/catch (warning), missing InvalidPluginExecutionException (warning),
        // missing InputParameters (warning), missing IPluginExecutionContext (warning)
        var code = @"
public class BadPlugin
{
    public async Task<string> Run()
    {
        var client = new HttpClient();
        Thread.Sleep(500);
        return ""done"";
    }
}";

        var result = await _tool.ValidatePluginCodeAsync(code, "BadPlugin", CancellationToken.None);

        Assert.True(result.Issues.Count(i => i.Severity == "error") >= 4);
        Assert.True(result.Score < 40);
        Assert.Equal("REJECTED", result.Verdict);
        Assert.False(result.IsValid);
        Assert.Equal("orange", result.RiskLevel);
    }
}
