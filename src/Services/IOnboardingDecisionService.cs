using IgorBot.Core;

namespace IgorBot.Services;

internal interface IOnboardingDecisionService
{
    Task<OnboardingDecisionResult> EvaluateAsync(
        Questionnaire questionnaire,
        IReadOnlyList<OnboardingAnswer> answers,
        ulong guildId,
        ulong memberId,
        CancellationToken cancellationToken = default);
}
