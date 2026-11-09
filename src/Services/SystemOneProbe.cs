using System.Diagnostics;

namespace IgorBot.Services;

internal enum SystemOneProbeStatus
{
    NotConfigured,
    Succeeded,
    Failed
}

internal sealed class SystemOneProbeReport
{
    public required SystemOneProbeStatus Status { get; init; }

    public string? Endpoint { get; init; }

    public string? Model { get; init; }

    public string? Label { get; init; }

    public IReadOnlyDictionary<string, double> Probabilities { get; init; } = new Dictionary<string, double>();

    public double? Confidence { get; init; }

    public double? Threshold { get; init; }

    public OnboardingDecisionOutcome? Outcome { get; init; }

    public TimeSpan Elapsed { get; init; }

    public string? Failure { get; init; }

    public bool UsedAuthorization { get; init; }
}

internal interface ISystemOneProbe
{
    Task<SystemOneProbeReport> ProbeAsync(CancellationToken cancellationToken = default);

    Task<SystemOneProbeReport> SimulateAsync(
        OnboardingSubmission submission,
        double threshold,
        CancellationToken cancellationToken = default);
}

internal sealed class SystemOneProbe(SystemOneSettings settings, IOnboardingDecisionClient client) : ISystemOneProbe
{
    internal static readonly OnboardingSubmission Sample = new()
    {
        QuestionnaireName = "On-boarding",
        Description = "Connection test",
        Context = "DsHidMini and related Nefarius projects.",
        Answers =
        [
            new OnboardingAnswer(
                "Have you read, understood and acknowledged our very simple server rules?",
                "Yes"),
            new OnboardingAnswer(
                "Why would you like to join?",
                "I want to follow DsHidMini development and report bugs.")
        ]
    };

    public Task<SystemOneProbeReport> ProbeAsync(CancellationToken cancellationToken = default) =>
        EvaluateAsync(Sample, 0.8, requireDecision: true, cancellationToken);

    public Task<SystemOneProbeReport> SimulateAsync(
        OnboardingSubmission submission,
        double threshold,
        CancellationToken cancellationToken = default) =>
        EvaluateAsync(submission, threshold, requireDecision: false, cancellationToken);

    private async Task<SystemOneProbeReport> EvaluateAsync(
        OnboardingSubmission submission,
        double threshold,
        bool requireDecision,
        CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured || settings.BaseUri is null)
        {
            return new SystemOneProbeReport
            {
                Status = SystemOneProbeStatus.NotConfigured,
                Model = settings.Model,
                Threshold = threshold,
                UsedAuthorization = settings.ApiToken is not null
            };
        }

        string endpoint = settings.BaseUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.SafeUnescaped);
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            SystemOneClassification classification = await client.ClassifyAsync(submission, cancellationToken);
            OnboardingDecisionResult validation = OnboardingDecisionPolicy.FromClassification(
                classification, threshold, DateTime.UtcNow);
            if (requireDecision && validation.Outcome == OnboardingDecisionOutcome.Unavailable)
            {
                throw new InvalidOperationException(validation.Failure);
            }

            watch.Stop();
            return new SystemOneProbeReport
            {
                Status = validation.Outcome == OnboardingDecisionOutcome.Unavailable
                    ? SystemOneProbeStatus.Failed
                    : SystemOneProbeStatus.Succeeded,
                Endpoint = endpoint,
                Model = classification.Model,
                Label = classification.Label,
                Probabilities = classification.Probabilities,
                Confidence = classification.Confidence,
                Threshold = threshold,
                Outcome = validation.Outcome,
                Elapsed = watch.Elapsed,
                Failure = validation.Failure,
                UsedAuthorization = settings.ApiToken is not null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            watch.Stop();
            return new SystemOneProbeReport
            {
                Status = SystemOneProbeStatus.Failed,
                Endpoint = endpoint,
                Model = settings.Model,
                Elapsed = watch.Elapsed,
                Failure = Redact(ex, settings.ApiToken),
                UsedAuthorization = settings.ApiToken is not null
            };
        }
    }

    private static string Redact(Exception exception, string? token)
    {
        string message = $"{exception.GetType().Name}: {exception.Message}";
        if (!string.IsNullOrEmpty(token))
        {
            message = message.Replace(token, "***", StringComparison.Ordinal);
        }

        return message.Length <= 500 ? message : message[..500];
    }
}
