using System.Collections.Concurrent;

namespace IgorBot.Util;

/// <summary>
///     Runs gateway follow-up work without hiding its exceptions. In-flight work is tracked so the host can wait
///     for it on shutdown (see <see cref="BackgroundWorkDrainService" />) instead of abandoning it mid-write.
/// </summary>
internal static class ObservableBackgroundWork
{
    private static readonly ConcurrentDictionary<Task, byte> InFlight = new();

    public static int InFlightCount => InFlight.Count;

    public static void Run(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken,
        ILogger logger,
        string operation)
    {
        Task task = Task.Run(async () =>
        {
            try
            {
                await work(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogDebug("Cancelled {Operation}", operation);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in {Operation}", operation);
            }
        }, CancellationToken.None);

        InFlight.TryAdd(task, 0);
        _ = task.ContinueWith(t => InFlight.TryRemove(t, out _), TaskScheduler.Default);
    }

    /// <summary>
    ///     Waits until all tracked work finished or <paramref name="timeout" /> elapsed.
    /// </summary>
    /// <returns>The number of tasks still running.</returns>
    public static async Task<int> DrainAsync(TimeSpan timeout)
    {
        Task[] pending = InFlight.Keys.ToArray();
        if (pending.Length == 0)
        {
            return 0;
        }

        await Task.WhenAny(Task.WhenAll(pending), Task.Delay(timeout));
        return InFlight.Count;
    }
}

/// <summary>
///     Registered before the Discord gateway service so it is stopped after it: once no new events arrive,
///     it waits for in-flight follow-up work to finish.
/// </summary>
internal sealed class BackgroundWorkDrainService(ILogger<BackgroundWorkDrainService> logger) : IHostedService
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(20);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        int active = ObservableBackgroundWork.InFlightCount;
        if (active == 0)
        {
            return;
        }

        logger.LogInformation("Waiting for {Count} background operation(s) to finish", active);
        int remaining = await ObservableBackgroundWork.DrainAsync(DrainTimeout);
        if (remaining > 0)
        {
            logger.LogWarning("{Count} background operation(s) did not finish before shutdown", remaining);
        }
    }
}
