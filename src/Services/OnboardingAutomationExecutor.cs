namespace IgorBot.Services;

internal interface IOnboardingAutomationExecutor
{
    Task<OnboardingAutomationEffect> ExecuteAsync(
        OnboardingDecisionResult decision,
        OnboardingAutomationState state,
        Func<Task> promoteAsync,
        CancellationToken cancellationToken = default);
}

internal sealed class OnboardingAutomationExecutor(ILogger<OnboardingAutomationExecutor> logger)
    : IOnboardingAutomationExecutor
{
    public async Task<OnboardingAutomationEffect> ExecuteAsync(
        OnboardingDecisionResult decision,
        OnboardingAutomationState state,
        Func<Task> promoteAsync,
        CancellationToken cancellationToken = default)
    {
        if (!OnboardingAutomationPlanner.ShouldPromote(decision, state))
        {
            return OnboardingAutomationEffect.Held;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await promoteAsync();
            return OnboardingAutomationEffect.Promoted;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Automatic promotion failed");
            return OnboardingAutomationEffect.PromotionFailed;
        }
    }
}
