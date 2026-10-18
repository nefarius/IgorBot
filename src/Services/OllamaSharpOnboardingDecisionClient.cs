using System.Net.Http.Headers;

using OllamaSharp;
using OllamaSharp.Models;

namespace IgorBot.Services;

internal sealed class OllamaSharpOnboardingDecisionClient : IOnboardingDecisionClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OllamaApiClient _client;
    private readonly string _model;

    public OllamaSharpOnboardingDecisionClient(SystemOneSettings settings)
        : this(settings, new HttpClientHandler())
    {
    }

    internal OllamaSharpOnboardingDecisionClient(SystemOneSettings settings, HttpMessageHandler handler)
    {
        if (settings.BaseUri is null)
        {
            throw new InvalidOperationException("System One endpoint is not configured.");
        }

        _model = settings.Model;
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = settings.BaseUri,
            Timeout = settings.Timeout
        };
        if (settings.ApiToken is not null)
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiToken);
        }

        _client = new OllamaApiClient(_httpClient, settings.Model);
    }

    public async Task<SystemOneClassification> ClassifyAsync(
        OnboardingSubmission submission,
        CancellationToken cancellationToken = default)
    {
        SystemOneRequest request = OnboardingDecisionRequestFactory.Create(submission, _model);
        SystemOneResponse response = await _client.SystemOneAsync(request, cancellationToken);
        if (!response.Answers.TryGetValue(OnboardingDecisionLabels.QuestionId, out SystemOneAnswer? answer) ||
            answer is not SystemOneChoiceAnswer choice ||
            string.IsNullOrWhiteSpace(choice.Choice) ||
            choice.Probabilities is null)
        {
            throw new InvalidOperationException("System One response did not contain a verdict.");
        }

        double? confidence = double.IsFinite(choice.Confidence) ? choice.Confidence : null;
        return new SystemOneClassification
        {
            Model = string.IsNullOrWhiteSpace(response.Model) ? _model : response.Model,
            Label = choice.Choice,
            Probabilities = choice.Probabilities,
            Confidence = confidence
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
    }
}

internal sealed class UnconfiguredOnboardingDecisionClient : IOnboardingDecisionClient
{
    public Task<SystemOneClassification> ClassifyAsync(
        OnboardingSubmission submission,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("System One endpoint is not configured.");
}
