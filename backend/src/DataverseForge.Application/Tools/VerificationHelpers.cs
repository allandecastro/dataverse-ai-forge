using DataverseForge.Domain.Models;

namespace DataverseForge.Application.Tools;

/// <summary>
/// Shared helpers for the Verify*AgainstPlan tools. The discrepancy comparison
/// and the "retry a metadata read that 404s transiently right after a write"
/// loop were copy-pasted (verbatim) across several verifiers.
/// </summary>
internal static class VerificationHelpers
{
    // Dataverse takes a moment to make freshly-written metadata queryable.
    // Retry on transient failures so verification doesn't false-FAIL right
    // after creation.
    private static readonly int[] DefaultRetryDelaysMs = [0, 1000, 2000];

    /// <summary>Adds a <see cref="Discrepancy"/> when expected != actual (case-insensitive).</summary>
    public static void Compare(List<Discrepancy> discrepancies, string property, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            discrepancies.Add(new Discrepancy(property, expected, actual));
    }

    /// <summary>
    /// Runs <paramref name="read"/> with a small backoff schedule, returning the
    /// value on first success or <c>(null, lastError)</c> if every attempt threw.
    /// </summary>
    public static async Task<(T? Value, Exception? LastError)> RetryReadAsync<T>(
        Func<Task<T>> read,
        CancellationToken cancellationToken,
        int[]? delaysMs = null) where T : class
    {
        Exception? lastError = null;
        foreach (var delayMs in delaysMs ?? DefaultRetryDelaysMs)
        {
            if (delayMs > 0) await Task.Delay(delayMs, cancellationToken);
            try
            {
                return (await read(), null);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        return (null, lastError);
    }
}
