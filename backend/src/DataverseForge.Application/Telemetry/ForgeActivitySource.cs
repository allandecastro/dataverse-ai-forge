using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DataverseForge.Application.Telemetry;

/// <summary>
/// Central ActivitySource and Meter for Dataverse AI Forge telemetry.
/// Use ForgeActivitySource.Source.StartActivity(...) for custom spans.
/// Use ForgeActivitySource.Meter for custom metrics.
/// </summary>
public static class ForgeActivitySource
{
    public const string ServiceName = "DataverseForge";
    public const string ServiceVersion = "1.0.0";

    public static readonly ActivitySource Source = new(ServiceName, ServiceVersion);
    public static readonly Meter Meter = new(ServiceName, ServiceVersion);

    // ─── COUNTERS ───
    public static readonly Counter<long> AgentTurns = Meter.CreateCounter<long>(
        "forge.agent.turns", "turns", "Number of agent turns executed");

    public static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>(
        "forge.tool.calls", "calls", "Number of tool calls executed");

    public static readonly Counter<long> HandoffCount = Meter.CreateCounter<long>(
        "forge.handoff.count", "handoffs", "Number of agent handoffs");

    public static readonly Counter<long> HandoffBlockedCount = Meter.CreateCounter<long>(
        "forge.handoff.blocked", "handoffs", "Number of invalid handoffs blocked");

    public static readonly Counter<long> UndoCount = Meter.CreateCounter<long>(
        "forge.undo.count", "undos", "Number of undo operations executed");

    public static readonly Counter<long> ToolErrors = Meter.CreateCounter<long>(
        "forge.tool.errors", "errors", "Number of tool execution errors");

    // ─── HISTOGRAMS ───
    public static readonly double[] LatencyBucketBoundaries =
        [10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000];

    public static readonly Histogram<double> DataverseApiDuration = Meter.CreateHistogram<double>(
        "forge.dataverse.api.duration", "ms", "Duration of Dataverse API calls");

    public static readonly Histogram<double> AgentTurnDuration = Meter.CreateHistogram<double>(
        "forge.agent.turn.duration", "ms", "Duration of agent turn processing");

    public static readonly Histogram<double> SseStreamDuration = Meter.CreateHistogram<double>(
        "forge.sse.stream.duration", "ms", "Duration of SSE stream processing");

    public static readonly Histogram<double> ToolExecutionDuration = Meter.CreateHistogram<double>(
        "forge.tool.execution.duration", "ms", "Duration of individual tool executions");
}
