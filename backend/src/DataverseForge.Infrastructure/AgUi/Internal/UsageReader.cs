using Microsoft.Extensions.AI;

namespace DataverseForge.Infrastructure.AgUi.Internal;

/// <summary>
/// Accumulates token-usage from a streaming response. Microsoft.Extensions.AI surfaces
/// usage primarily via <see cref="UsageContent"/> in <c>update.Contents</c>; some
/// providers also expose it via <c>AdditionalProperties</c>. We read both.
///
/// Past regression: only reading AdditionalProperties left Session Cost stuck at $0.
/// </summary>
internal sealed class UsageReader
{
    private int? _input;
    private int? _output;

    public bool HasUsage => _input.HasValue || _output.HasValue;
    public int InputTokens => _input ?? 0;
    public int OutputTokens => _output ?? 0;
    public int TotalTokens => InputTokens + OutputTokens;

    public void Ingest(ChatResponseUpdate update)
    {
        if (update.Contents != null)
        {
            foreach (var c in update.Contents)
            {
                if (c is UsageContent uc && uc.Details is { } d)
                {
                    if (d.InputTokenCount.HasValue)
                        _input = (_input ?? 0) + (int)d.InputTokenCount.Value;
                    if (d.OutputTokenCount.HasValue)
                        _output = (_output ?? 0) + (int)d.OutputTokenCount.Value;
                }
            }
        }
        if (update.AdditionalProperties != null)
        {
            if (update.AdditionalProperties.TryGetValue("inputTokens", out var inp) && inp is int inpVal)
                _input = inpVal;
            if (update.AdditionalProperties.TryGetValue("outputTokens", out var outp) && outp is int outpVal)
                _output = outpVal;
        }
    }
}
