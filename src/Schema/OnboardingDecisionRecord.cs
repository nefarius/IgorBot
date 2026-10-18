using System.Globalization;

namespace IgorBot.Schema;

/// <summary>
///     Persisted System One screening result. Answers are not stored.
/// </summary>
internal sealed class OnboardingDecisionRecord
{
    public string Outcome { get; set; } = "";

    public string? Model { get; set; }

    public string? Label { get; set; }

    public Dictionary<string, double> Probabilities { get; set; } = new();

    public double? Confidence { get; set; }

    public double? Threshold { get; set; }

    public DateTime EvaluatedAt { get; set; }

    public bool AutomationAttempted { get; set; }

    public bool AutoPromoted { get; set; }

    public string? Failure { get; set; }

    /// <summary>
    ///     Moderator-facing summary for the status widget.
    /// </summary>
    public string ToScreeningText()
    {
        try
        {
            if (Outcome == "Unavailable")
            {
                return ManualReviewText;
            }

            string action = AutoPromoted ? "automatically promoted" : "manual review";
            return string.Join('\n',
            [
                $"Recommendation: {Label} ({Percent(Probability(Label))})",
                $"Probabilities: {FormatProbabilities()}",
                $"Confidence: {Percent(Confidence)}",
                $"Threshold: {Percent(Threshold)}",
                $"Action: {action}"
            ]);
        }
        catch (Exception)
        {
            return ManualReviewText;
        }
    }

    private const string ManualReviewText = "Recommendation: manual review\nClassifier unavailable.";

    private string FormatProbabilities()
    {
        string[] order = ["normal", "bot", "reject"];
        Dictionary<string, double> probabilities = Probabilities ?? new Dictionary<string, double>();
        IEnumerable<string> labels = order.Where(probabilities.ContainsKey)
            .Concat(probabilities.Keys.Where(label => !order.Contains(label, StringComparer.OrdinalIgnoreCase)));
        return string.Join(", ", labels.Select(label => $"{label} {Percent(probabilities[label])}"));
    }

    private double? Probability(string? label) =>
        label is not null && Probabilities.TryGetValue(label, out double probability) ? probability : null;

    private static string Percent(double? value) =>
        value is null ? "n/a" : value.Value.ToString("P0", CultureInfo.InvariantCulture);
}
