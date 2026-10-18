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

    public TimeSpan Elapsed { get; init; }

    public string? Failure { get; init; }

    public bool UsedAuthorization { get; init; }
}

internal interface ISystemOneProbe
{
    Task<SystemOneProbeReport> ProbeAsync(CancellationToken cancellationToken = default);
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

    public async Task<SystemOneProbeReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || settings.BaseUri is null)
        {
            return new SystemOneProbeReport
            {
                Status = SystemOneProbeStatus.NotConfigured,
                Model = settings.Model,
                UsedAuthorization = settings.ApiToken is not null
            };
        }

        string endpoint = settings.BaseUri.GetLeftPart(UriPartial.Authority);
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            SystemOneClassification classification = await client.ClassifyAsync(Sample, cancellationToken);
            watch.Stop();
            return new SystemOneProbeReport
            {
                Status = SystemOneProbeStatus.Succeeded,
                Endpoint = endpoint,
                Model = classification.Model,
                Label = classification.Label,
                Probabilities = classification.Probabilities,
                Confidence = classification.Confidence,
                Elapsed = watch.Elapsed,
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
