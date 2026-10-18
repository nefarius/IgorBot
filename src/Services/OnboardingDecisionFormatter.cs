using System.Globalization;

namespace IgorBot.Services;

internal static class OnboardingDecisionFormatter
{
    public const string FieldName = "Screening";

    public static string Format(
        OnboardingDecisionResult decision,
        bool willPromote,
        bool autoPromoted,
        bool promotionFailed)
    {
        if (decision.Outcome == OnboardingDecisionOutcome.Unavailable)
        {
            return "Recommendation: manual review\nClassifier unavailable.";
        }

        return string.Join('\n',
        [
            $"Recommendation: {decision.Label} ({Percent(Probability(decision, decision.Label))})",
            $"Probabilities: {FormatProbabilities(decision.Probabilities)}",
            $"Confidence: {Percent(decision.Confidence)}",
            $"Threshold: {Percent(decision.Threshold)}",
            $"Action: {Action(willPromote, autoPromoted, promotionFailed)}"
        ]);
    }

    private static string Action(bool willPromote, bool autoPromoted, bool promotionFailed)
    {
        if (autoPromoted)
        {
            return "automatically promoted";
        }

        if (promotionFailed)
        {
            return "automatic promotion failed; manual review";
        }

        return willPromote ? "automatic promotion" : "manual review";
    }

    private static string FormatProbabilities(IReadOnlyDictionary<string, double> probabilities)
    {
        IEnumerable<string> labels = OnboardingDecisionLabels.DisplayOrder
            .Where(probabilities.ContainsKey)
            .Concat(probabilities.Keys.Where(label =>
                !OnboardingDecisionLabels.DisplayOrder.Contains(label, StringComparer.OrdinalIgnoreCase)));

        return string.Join(", ", labels.Select(label => $"{label} {Percent(probabilities[label])}"));
    }

    private static double? Probability(OnboardingDecisionResult decision, string? label) =>
        Probability(decision.Probabilities, label);

    private static double? Probability(IReadOnlyDictionary<string, double> probabilities, string? label)
    {
        if (label is null)
        {
            return null;
        }

        return probabilities.TryGetValue(label, out double probability) ? probability : null;
    }

    private static string Percent(double? value) =>
        value is null ? "n/a" : value.Value.ToString("P0", CultureInfo.InvariantCulture);
}
