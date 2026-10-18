namespace IgorBot.Services;

internal interface IOnboardingDecisionClient
{
    Task<SystemOneClassification> ClassifyAsync(
        OnboardingSubmission submission,
        CancellationToken cancellationToken = default);
}
