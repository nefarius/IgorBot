using IgorBot.Core;

namespace IgorBot.Services;

/// <summary>
///     Queue for serialized processing of new member onboarding events.
/// </summary>
internal interface IOnboardingQueue
{
    ValueTask EnqueueAsync(NewMemberMessage message, CancellationToken ct = default);
}
