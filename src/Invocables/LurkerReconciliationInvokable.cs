using Coravel.Invocable;

using IgorBot.Services;

namespace IgorBot.Invocables;

/// <summary>
///     Daily job that heals Lurker/Stranger members missing their channel and status widget.
/// </summary>
internal sealed class LurkerReconciliationInvokable(
    ILurkerReconciliationService reconciliation,
    ILogger<LurkerReconciliationInvokable> logger) : IInvocable
{
    public async Task Invoke()
    {
        try
        {
            await reconciliation.ReconcileAllAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scheduled lurker reconciliation failed");
        }
    }
}
