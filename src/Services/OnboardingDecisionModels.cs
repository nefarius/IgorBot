using IgorBot.Schema;

namespace IgorBot.Services;

internal enum OnboardingDecisionOutcome
{
    Disabled,
    Unavailable,
    ManualReview,
    AutoPromote
}

internal readonly record struct OnboardingAnswer(string Question, string Answer);

internal sealed class OnboardingSubmission
{
    public required string QuestionnaireName { get; init; }

    public required string Description { get; init; }

    public string? Context { get; init; }

    public required IReadOnlyList<OnboardingAnswer> Answers { get; init; }
}

internal sealed class SystemOneClassification
{
    public required string Model { get; init; }

    public required string Label { get; init; }

    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }

    public double? Confidence { get; init; }
}

internal sealed record OnboardingDecisionResult
{
    public required OnboardingDecisionOutcome Outcome { get; init; }

    public string? Model { get; init; }

    public string? Label { get; init; }

    public IReadOnlyDictionary<string, double> Probabilities { get; init; } =
        new Dictionary<string, double>();

    public double? Confidence { get; init; }

    public double? Threshold { get; init; }

    public DateTime EvaluatedAt { get; init; }

    public string? Failure { get; init; }

    public OnboardingDecisionRecord ToRecord(bool automationAttempted, bool autoPromoted)
    {
        return new OnboardingDecisionRecord
        {
            Outcome = Outcome.ToString(),
            Model = Model,
            Label = Label,
            Probabilities = new Dictionary<string, double>(Probabilities),
            Confidence = Confidence,
            Threshold = Threshold,
            EvaluatedAt = EvaluatedAt,
            AutomationAttempted = automationAttempted,
            AutoPromoted = autoPromoted,
            Failure = Failure
        };
    }
}

internal readonly record struct OnboardingAutomationState(
    bool AutomationAttempted,
    MemberStatus Status,
    bool HasStrangerRole);

internal enum OnboardingAutomationEffect
{
    Held,
    Promoted,
    PromotionFailed
}
