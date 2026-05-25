using System.Collections.Concurrent;
using System.Reflection;

namespace DataverseForge.Application.Agents;

/// <summary>
/// Loads agent system prompts from embedded markdown resources under
/// <c>Agents/Prompts/*.md</c>. Replaces the older "inline C# raw-string-literal
/// per agent" shape where each prompt was a ~30 KB const string wrapped in
/// <c>"""..."""</c> with hand-drawn <c>═══</c> dividers — those dividers shipped
/// to the model on every turn, and authoring markdown inside a C# raw string
/// made every edit a diff full of indentation noise.
///
/// Behaviour:
/// <list type="bullet">
///   <item>Resources are looked up by file name (e.g. <c>"BusinessAnalyst.md"</c>);
///         the namespace prefix is fixed.</item>
///   <item>Each prompt is loaded once and cached. The cost is amortised over
///         every chat turn this process serves.</item>
///   <item>A missing resource throws at startup with a helpful list of what
///         IS available — fail-fast beats "the agent silently has no prompt".</item>
/// </list>
///
/// To add a new agent prompt: drop <c>Agents/Prompts/NewAgent.md</c>, the csproj
/// glob picks it up automatically, then reference it from <c>NewAgentPrompt.cs</c>
/// via <c>EmbeddedPrompts.Load("NewAgent.md")</c>.
/// </summary>
internal static class EmbeddedPrompts
{
    private const string ResourceNamespace = "DataverseForge.Application.Agents.Prompts";
    private static readonly Assembly Assembly = typeof(EmbeddedPrompts).Assembly;
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    /// <summary>
    /// Loads and caches the named markdown prompt. <paramref name="fileName"/> is
    /// the leaf name (e.g. <c>"BusinessAnalyst.md"</c>), NOT a path — the
    /// resource namespace is fixed.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the resource is missing. The message lists every embedded
    /// resource the assembly knows about so a typo is obvious.
    /// </exception>
    public static string Load(string fileName)
    {
        return Cache.GetOrAdd(fileName, name =>
        {
            var resourceName = $"{ResourceNamespace}.{name}";
            using var stream = Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                var available = string.Join(", ", Assembly
                    .GetManifestResourceNames()
                    .Where(n => n.StartsWith(ResourceNamespace, StringComparison.Ordinal)));
                throw new InvalidOperationException(
                    $"Embedded prompt '{name}' not found. Expected resource '{resourceName}'. " +
                    $"Available under '{ResourceNamespace}': [{available}].");
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().TrimEnd() + "\n"; // canonical trailing newline
        });
    }

    /// <summary>
    /// Shared trailing section every agent prompt appends after its main
    /// content + CommonPromptFragments composition. Holds the placeholder the
    /// runtime substitutes with the current ProjectState snapshot.
    /// </summary>
    public const string ProjectStateTrailer = """
        ## Project State

        [INJECTED AT RUNTIME]
        """;
}
