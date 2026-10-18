using IgorBot.Schema;

namespace IgorBot.Services;

internal static class OnboardingDecisionLabels
{
    public const string Normal = "normal";

    public const string Bot = "bot";

    public const string Reject = "reject";

    public const string QuestionId = "verdict";

    public const string AutoPromoteReason = "system-one:auto-promote";

    public static readonly string[] DisplayOrder = [Normal, Bot, Reject];
}

internal static class OnboardingDecisionPolicy
{
    public static OnboardingDecisionResult Disabled(DateTime evaluatedAt) =>
        new()
        {
            Outcome = OnboardingDecisionOutcome.Disabled,
            EvaluatedAt = evaluatedAt
        };

    public static OnboardingDecisionResult Unavailable(DateTime evaluatedAt, string failure) =>
        new()
        {
            Outcome = OnboardingDecisionOutcome.Unavailable,
            EvaluatedAt = evaluatedAt,
            Failure = failure
        };

    public static OnboardingDecisionResult FromClassification(
        SystemOneClassification classification,
        double threshold,
        DateTime evaluatedAt)
    {
        if (!double.IsFinite(threshold) || threshold < 0 || threshold > 1)
        {
            return Unavailable(evaluatedAt, "Auto-promotion threshold is invalid.");
        }

        if (!TryReadProbabilities(classification, out Dictionary<string, double> probabilities,
                out double normalProbability))
        {
            return Unavailable(evaluatedAt, "Classifier response was incomplete.");
        }

        bool autoPromote = classification.Label.Equals(OnboardingDecisionLabels.Normal, StringComparison.OrdinalIgnoreCase) &&
                           normalProbability >= threshold;

        return new OnboardingDecisionResult
        {
            Outcome = autoPromote ? OnboardingDecisionOutcome.AutoPromote : OnboardingDecisionOutcome.ManualReview,
            Model = classification.Model,
            Label = classification.Label,
            Probabilities = probabilities,
            Confidence = classification.Confidence,
            Threshold = threshold,
            EvaluatedAt = evaluatedAt
        };
    }

    private static bool TryReadProbabilities(
        SystemOneClassification classification,
        out Dictionary<string, double> probabilities,
        out double normalProbability)
    {
        probabilities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        normalProbability = 0;
        if (string.IsNullOrWhiteSpace(classification.Label) || classification.Probabilities.Count == 0)
        {
            return false;
        }

        foreach ((string label, double probability) in classification.Probabilities)
        {
            if (string.IsNullOrWhiteSpace(label) || !IsProbability(probability))
            {
                return false;
            }

            probabilities[label] = probability;
        }

        if (classification.Confidence is not null && !double.IsFinite(classification.Confidence.Value))
        {
            return false;
        }

        foreach (string required in OnboardingDecisionLabels.DisplayOrder)
        {
            if (!probabilities.ContainsKey(required))
            {
                return false;
            }
        }

        if (!probabilities.TryGetValue(classification.Label, out _))
        {
            return false;
        }

        normalProbability = probabilities[OnboardingDecisionLabels.Normal];
        return true;
    }

    private static bool IsProbability(double probability) =>
        double.IsFinite(probability) && probability is >= 0 and <= 1;
}

internal static class OnboardingAutomationPlanner
{
    public static bool ShouldPromote(OnboardingDecisionResult decision, OnboardingAutomationState state)
    {
        if (decision.Outcome != OnboardingDecisionOutcome.AutoPromote || state.AutomationAttempted || !state.HasStrangerRole)
        {
            return false;
        }

        return state.Status is MemberStatus.Onboarding or MemberStatus.QuestionnaireSubmitted;
    }
}
