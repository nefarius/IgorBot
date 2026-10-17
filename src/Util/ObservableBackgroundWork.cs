namespace IgorBot.Util;

/// <summary>
///     Runs gateway follow-up work without hiding its exceptions.
/// </summary>
internal static class ObservableBackgroundWork
{
    public static void Run(
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken,
        ILogger logger,
        string operation)
    {
        _ = Task.Run(async () =>
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
    }
}
