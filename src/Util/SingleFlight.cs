using System.Collections.Concurrent;

namespace IgorBot.Util;

/// <summary>
///     Process-wide guard so a scheduled run and a manual run of the same job never overlap.
/// </summary>
internal static class SingleFlight
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    /// <summary>
    ///     Runs <paramref name="work" /> unless a run with the same <paramref name="key" /> is already active.
    /// </summary>
    /// <returns><c>true</c> if the work ran; <c>false</c> if it was skipped.</returns>
    public static async Task<bool> RunAsync(string key, Func<Task> work, ILogger logger)
    {
        SemaphoreSlim gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        if (!await gate.WaitAsync(TimeSpan.Zero))
        {
            logger.LogInformation("{Job} already running, skipping this run", key);
            return false;
        }

        try
        {
            await work();
            return true;
        }
        finally
        {
            gate.Release();
        }
    }
}
