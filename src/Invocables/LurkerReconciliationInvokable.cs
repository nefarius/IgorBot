using Coravel.Invocable;
using Coravel.Queuing.Interfaces;

using IgorBot.Services;

namespace IgorBot.Invocables;

/// <summary>
///     Daily job that heals Lurker/Stranger members missing their channel and status widget.
/// </summary>
internal sealed class LurkerReconciliationInvokable(
    ILurkerReconciliationService reconciliation,
    ILogger<LurkerReconciliationInvokable> logger) : IInvocable, ICancellableInvocable, ICancellableTask
{
    // Scheduler sets CancellationToken, the queue sets Token; both feed the same value.
    private CancellationToken _token;

    public CancellationToken CancellationToken
    {
        get => _token;
        set => _token = value;
    }

    public CancellationToken Token
    {
        get => _token;
        set => _token = value;
    }

    public async Task Invoke()
    {
        try
        {
            await reconciliation.ReconcileAllAsync(CancellationToken);
        }
        catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Lurker reconciliation cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled lurker reconciliation failed");
        }
    }
}
