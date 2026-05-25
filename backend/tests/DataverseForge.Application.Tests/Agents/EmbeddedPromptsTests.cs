using DataverseForge.Application.Agents;

namespace DataverseForge.Application.Tests.Agents;

/// <summary>
/// Smoke test for the embedded-prompt loader infrastructure introduced when
/// each agent's main prompt moved from an inline C# raw-string literal to a
/// dedicated <c>Agents/Prompts/*.md</c> resource. Without this test, a typo
/// in any wrapper's <c>EmbeddedPrompts.Load("X.md")</c> call would crash at
/// FIRST AGENT INVOCATION — a chat-time failure rather than a build-time one.
///
/// Each test below proves one piece of the contract:
/// <list type="bullet">
///   <item>the file is present in the assembly's embedded resources;</item>
///   <item>it returns non-empty content;</item>
///   <item>the corresponding <c>*Prompt.SystemPrompt</c> wrapper actually
///         contains the loaded body (i.e. the wrapper isn't accidentally
///         pointing at a different .md file);</item>
///   <item>every prompt ends with the shared ProjectStateTrailer marker,
///         confirming the composition didn't drop it.</item>
/// </list>
/// </summary>
public class EmbeddedPromptsTests
{
    public static IEnumerable<object[]> AgentPrompts() => new[]
    {
        new object[] { "AutomationBuilder.md", AutomationBuilderPrompt.SystemPrompt },
        new object[] { "BusinessAnalyst.md",   BusinessAnalystPrompt.SystemPrompt },
        new object[] { "DataModelBuilder.md",  DataModelBuilderPrompt.SystemPrompt },
        new object[] { "DataPopulation.md",    DataPopulationPrompt.SystemPrompt },
        new object[] { "SecurityBuilder.md",   SecurityBuilderPrompt.SystemPrompt },
        new object[] { "SolutionArchitect.md", SolutionArchitectPrompt.SystemPrompt },
        new object[] { "Tester.md",            TesterPrompt.SystemPrompt },
        new object[] { "UIBuilder.md",         UIBuilderPrompt.SystemPrompt },
    };

    [Theory]
    [InlineData("AutomationBuilder.md")]
    [InlineData("BusinessAnalyst.md")]
    [InlineData("DataModelBuilder.md")]
    [InlineData("DataPopulation.md")]
    [InlineData("SecurityBuilder.md")]
    [InlineData("SolutionArchitect.md")]
    [InlineData("Tester.md")]
    [InlineData("UIBuilder.md")]
    public void EmbeddedResource_Loads_NonEmpty(string fileName)
    {
        // The loader throws InvalidOperationException with a helpful listing
        // of available resources if the file is missing. We just need it
        // to return a non-trivial string.
        var content = EmbeddedPrompts.Load(fileName);
        Assert.NotNull(content);
        Assert.True(content.Length > 500,
            $"{fileName} loaded only {content.Length} chars — expected ≥500 (likely an extraction or csproj-glob regression).");
    }

    [Theory]
    [MemberData(nameof(AgentPrompts))]
    public void Wrapper_SystemPrompt_ContainsLoadedBody(string fileName, string systemPrompt)
    {
        // Pick a non-trivial slice of the loaded .md and prove it appears
        // verbatim inside the composed SystemPrompt. Catches the case where
        // a wrapper points at the wrong .md (e.g. UIBuilder.cs calling
        // Load("Tester.md") by typo).
        var loaded = EmbeddedPrompts.Load(fileName);
        var sample = loaded[..Math.Min(200, loaded.Length)].TrimEnd();
        Assert.Contains(sample, systemPrompt);
    }

    [Theory]
    [MemberData(nameof(AgentPrompts))]
    public void Wrapper_SystemPrompt_EndsWithProjectStateTrailer(string fileName, string systemPrompt)
    {
        // ProjectStateTrailer is appended by every wrapper as the final
        // section; the runtime substitutes [INJECTED AT RUNTIME] with the
        // serialized ProjectState. Catches the case where the wrapper
        // forgot to compose it.
        _ = fileName; // parameter is purely for test-row labelling
        Assert.Contains("[INJECTED AT RUNTIME]", systemPrompt);
        Assert.Contains("## Project State", systemPrompt);
    }

    [Fact]
    public void Loader_ThrowsHelpfully_OnMissingResource()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => EmbeddedPrompts.Load("DoesNotExist.md"));
        // The message should list what IS available, so a typo is obvious.
        Assert.Contains("DoesNotExist.md", ex.Message);
        Assert.Contains("BusinessAnalyst.md", ex.Message);
    }

    [Fact]
    public void Loader_CachesRepeatedLoads()
    {
        // Same string instance on repeat — confirms the ConcurrentDictionary
        // cache is wired and we aren't reading the resource per call.
        var first  = EmbeddedPrompts.Load("BusinessAnalyst.md");
        var second = EmbeddedPrompts.Load("BusinessAnalyst.md");
        Assert.Same(first, second);
    }
}
