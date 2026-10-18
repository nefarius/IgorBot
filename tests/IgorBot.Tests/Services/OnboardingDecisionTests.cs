using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;

using Microsoft.Extensions.Logging.Abstractions;

using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace IgorBot.Tests.Services;

public sealed class OnboardingDecisionPolicyTests
{
    private static readonly DateTime EvaluatedAt = new(2026, 10, 18, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Disabled_DoesNotClassify()
    {
        OnboardingDecisionResult result = OnboardingDecisionPolicy.Disabled(EvaluatedAt);

        result.Outcome.Should().Be(OnboardingDecisionOutcome.Disabled);
        result.Label.Should().BeNull();
    }

    [Theory]
    [InlineData(0.80, 0.80)]
    [InlineData(0.91, 0.80)]
    public void NormalAtOrAboveThreshold_Promotes(double probability, double threshold)
    {
        OnboardingDecisionResult result = Decide(probability, threshold, OnboardingDecisionLabels.Normal);

        result.Outcome.Should().Be(OnboardingDecisionOutcome.AutoPromote);
        result.Threshold.Should().Be(threshold);
    }

    [Fact]
    public void NormalBelowThreshold_StaysInReview()
    {
        Decide(0.799, 0.80, OnboardingDecisionLabels.Normal).Outcome
            .Should().Be(OnboardingDecisionOutcome.ManualReview);
    }

    [Theory]
    [InlineData("bot")]
    [InlineData("reject")]
    public void NonNormalLabel_StaysInReview(string label)
    {
        Decide(0.99, 0.80, label).Outcome.Should().Be(OnboardingDecisionOutcome.ManualReview);
    }

    [Fact]
    public void MalformedProbability_FailsOpen()
    {
        SystemOneClassification classification = Classification(OnboardingDecisionLabels.Normal, normal: double.NaN);
        OnboardingDecisionPolicy.FromClassification(classification, 0.80, EvaluatedAt).Outcome
            .Should().Be(OnboardingDecisionOutcome.Unavailable);
    }

    [Fact]
    public void MissingCandidate_FailsOpen()
    {
        SystemOneClassification classification = new()
        {
            Model = "loaded",
            Label = OnboardingDecisionLabels.Normal,
            Probabilities = new Dictionary<string, double> { ["normal"] = 1 },
            Confidence = 1
        };

        OnboardingDecisionPolicy.FromClassification(classification, 0.80, EvaluatedAt).Outcome
            .Should().Be(OnboardingDecisionOutcome.Unavailable);
    }

    [Fact]
    public void InvalidThreshold_FailsOpen()
    {
        OnboardingDecisionPolicy.FromClassification(
                Classification(OnboardingDecisionLabels.Normal, 1), 1.2, EvaluatedAt).Outcome
            .Should().Be(OnboardingDecisionOutcome.Unavailable);
    }

    private static OnboardingDecisionResult Decide(double selectedProbability, double threshold, string label)
    {
        double normal = label == OnboardingDecisionLabels.Normal ? selectedProbability : (1 - selectedProbability) / 2;
        double other = label == OnboardingDecisionLabels.Normal ? (1 - selectedProbability) / 2 : selectedProbability;
        SystemOneClassification classification = label switch
        {
            OnboardingDecisionLabels.Bot => Classification(label, normal, bot: other, reject: (1 - selectedProbability) / 2),
            OnboardingDecisionLabels.Reject => Classification(label, normal, bot: (1 - selectedProbability) / 2, reject: other),
            _ => Classification(label, selectedProbability)
        };
        return OnboardingDecisionPolicy.FromClassification(classification, threshold, EvaluatedAt);
    }

    private static SystemOneClassification Classification(
        string label,
        double normal,
        double? bot = null,
        double? reject = null)
    {
        double remainder = (1 - normal) / 2;
        return new SystemOneClassification
        {
            Model = "loaded",
            Label = label,
            Confidence = 0.75,
            Probabilities = new Dictionary<string, double>
            {
                [OnboardingDecisionLabels.Normal] = normal,
                [OnboardingDecisionLabels.Bot] = bot ?? remainder,
                [OnboardingDecisionLabels.Reject] = reject ?? remainder
            }
        };
    }
}

public sealed class OnboardingDecisionServiceTests
{
    [Fact]
    public async Task DisabledQuestionnaire_DoesNotCallTheEndpoint()
    {
        FakeDecisionClient client = new();
        OnboardingDecisionService service = Service(client, configured: true);

        OnboardingDecisionResult result = await service.EvaluateAsync(Questionnaire(false), [], 1, 2);

        result.Outcome.Should().Be(OnboardingDecisionOutcome.Disabled);
        client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task MissingEndpoint_FailsOpenWithoutCalling()
    {
        FakeDecisionClient client = new();
        OnboardingDecisionService service = Service(client, configured: false);

        OnboardingDecisionResult result = await service.EvaluateAsync(Questionnaire(true), [], 1, 2);

        result.Outcome.Should().Be(OnboardingDecisionOutcome.Unavailable);
        client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task NetworkFailure_StaysInModeratorReview()
    {
        FakeDecisionClient client = new() { Error = new HttpRequestException("connection refused") };
        OnboardingDecisionResult result = await Service(client, configured: true)
            .EvaluateAsync(Questionnaire(true), [new OnboardingAnswer("Why join?", "bugs")], 4, 5);

        result.Outcome.Should().Be(OnboardingDecisionOutcome.Unavailable);
        OnboardingAutomationPlanner.ShouldPromote(
                result, new OnboardingAutomationState(false, MemberStatus.QuestionnaireSubmitted, true))
            .Should().BeFalse();
        result.ToRecord(automationAttempted: false, autoPromoted: false).ToScreeningText()
            .Should().Contain("manual review");
    }

    [Fact]
    public async Task ClientFailure_FailsOpen()
    {
        FakeDecisionClient client = new() { Error = new HttpRequestException("down") };
        OnboardingDecisionService service = Service(client, configured: true);

        OnboardingDecisionResult result = await service.EvaluateAsync(Questionnaire(true), [], 9, 8);

        result.Outcome.Should().Be(OnboardingDecisionOutcome.Unavailable);
        result.Failure.Should().Contain(nameof(HttpRequestException));
        client.Calls.Should().Be(1);
        client.LastSubmission!.Answers.Should().BeEmpty();
    }

    private static OnboardingDecisionService Service(FakeDecisionClient client, bool configured)
    {
        SystemOneSettings settings = SystemOneSettings.Create(new SystemOneConfig
        {
            BaseUrl = configured ? "http://192.168.30.187:8081" : "",
            Model = "loaded"
        });
        return new OnboardingDecisionService(settings, client, NullLogger<OnboardingDecisionService>.Instance);
    }

    private static Questionnaire Questionnaire(bool enabled) => new()
    {
        Id = "newbie",
        Name = "On-boarding",
        Description = "New members",
        Automation = new QuestionnaireAutomation
        {
            Enabled = enabled,
            Context = "DsHidMini",
            AutoPromoteNormalProbability = 0.80
        }
    };

    private sealed class FakeDecisionClient : IOnboardingDecisionClient
    {
        public int Calls { get; private set; }

        public Exception? Error { get; init; }

        public OnboardingSubmission? LastSubmission { get; private set; }

        public Task<SystemOneClassification> ClassifyAsync(
            OnboardingSubmission submission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSubmission = submission;
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(new SystemOneClassification
            {
                Model = "loaded",
                Label = OnboardingDecisionLabels.Normal,
                Confidence = 1,
                Probabilities = new Dictionary<string, double>
                {
                    [OnboardingDecisionLabels.Normal] = 1,
                    [OnboardingDecisionLabels.Bot] = 0,
                    [OnboardingDecisionLabels.Reject] = 0
                }
            });
        }
    }
}

public sealed class OnboardingDecisionClientTests
{
    [Fact]
    public void RequestState_OmitsDiscordIdentity()
    {
        const string discordId = "976144920576425995";
        SystemOneRequestState state = new(discordId);
        string json = JsonSerializer.Serialize(OnboardingDecisionRequestFactory.Create(state.Submission, "loaded").State);

        json.Should().Contain("project documentation");
        json.Should().NotContain(discordId);
        json.Should().NotContain(state.Username);
    }

    [Theory]
    [InlineData("secret-token", "Bearer secret-token")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task AuthorizationHeader_IsSentOnlyForAToken(string? token, string? expected)
    {
        RecordingHandler handler = new();
        SystemOneSettings settings = SystemOneSettings.Create(new SystemOneConfig
        {
            BaseUrl = "http://127.0.0.1:9",
            Model = "loaded",
            ApiToken = token,
            TimeoutSeconds = 2
        });
        using OllamaSharpOnboardingDecisionClient client = new(settings, handler);

        SystemOneClassification result = await client.ClassifyAsync(new SystemOneRequestState("unused").Submission);

        result.Label.Should().Be(OnboardingDecisionLabels.Normal);
        handler.Request!.RequestUri!.AbsolutePath.Should().Be("/v1/systemone");
        string? authorization = handler.Request.Headers.Authorization?.ToString();
        authorization.Should().Be(expected);
        handler.Body.Should().Contain("project documentation");
        handler.Body.Should().NotContain("976144920576425995");
    }

    private sealed record SystemOneRequestState(string DiscordId)
    {
        public string Username { get; } = "passiveaggresive";

        public OnboardingSubmission Submission { get; } = new()
        {
            QuestionnaireName = "On-boarding",
            Description = "New members on-boarding process",
            Context = "DsHidMini and related Nefarius projects.",
            Answers =
            [
                new OnboardingAnswer("How/Where did you hear about this Discord server?", "project documentation")
            ]
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            string json = """
                          {
                            "model": "/data/PS5LM/models/d1-omni-600M-BF16.gguf",
                            "answers": {
                              "verdict": {
                                "type": "choice",
                                "choice": "normal",
                                "probabilities": { "normal": 0.9, "bot": 0.05, "reject": 0.05 },
                                "confidence": 0.8
                              }
                            },
                            "usage": { "input_tokens": 12, "output_tokens": 0 }
                          }
                          """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}

public sealed class OnboardingAutomationExecutorTests
{
    [Fact]
    public async Task OnlyAQualifyingNormalDecisionPromotes()
    {
        PromotionDouble promotion = new();
        OnboardingAutomationExecutor executor = Executor();
        OnboardingDecisionResult decision = AutoPromote();

        OnboardingAutomationEffect effect = await executor.ExecuteAsync(
            decision, Eligible(), promotion.PromoteAsync);

        effect.Should().Be(OnboardingAutomationEffect.Promoted);
        promotion.Promotions.Should().Be(1);
        promotion.Removals.Should().Be(0);
    }

    [Fact]
    public async Task RepeatedExecution_DoesNotPromoteAgain()
    {
        PromotionDouble promotion = new();
        OnboardingAutomationExecutor executor = Executor();

        OnboardingAutomationEffect effect = await executor.ExecuteAsync(
            AutoPromote(),
            Eligible() with { AutomationAttempted = true },
            promotion.PromoteAsync);

        effect.Should().Be(OnboardingAutomationEffect.Held);
        promotion.Promotions.Should().Be(0);
        promotion.Removals.Should().Be(0);
    }

    [Theory]
    [InlineData(nameof(OnboardingDecisionOutcome.ManualReview))]
    [InlineData(nameof(OnboardingDecisionOutcome.Unavailable))]
    [InlineData(nameof(OnboardingDecisionOutcome.Disabled))]
    public async Task NonPromotionOutcomes_DoNotRemoveTheMember(string outcomeName)
    {
        PromotionDouble promotion = new();
        OnboardingDecisionOutcome outcome = Enum.Parse<OnboardingDecisionOutcome>(outcomeName);
        OnboardingDecisionResult decision = AutoPromote() with { Outcome = outcome };

        OnboardingAutomationEffect effect = await Executor().ExecuteAsync(decision, Eligible(), promotion.PromoteAsync);

        effect.Should().Be(OnboardingAutomationEffect.Held);
        promotion.Promotions.Should().Be(0);
        promotion.Removals.Should().Be(0);
    }

    [Fact]
    public async Task PromotionFailure_StaysWithModerators()
    {
        PromotionDouble promotion = new() { FailPromotion = true };

        OnboardingAutomationEffect effect = await Executor().ExecuteAsync(
            AutoPromote(), Eligible(), promotion.PromoteAsync);

        effect.Should().Be(OnboardingAutomationEffect.PromotionFailed);
        promotion.Removals.Should().Be(0);
    }

    private static OnboardingAutomationExecutor Executor() =>
        new(NullLogger<OnboardingAutomationExecutor>.Instance);

    private static OnboardingAutomationState Eligible() =>
        new(false, MemberStatus.Onboarding, true);

    private static OnboardingDecisionResult AutoPromote() => new()
    {
        Outcome = OnboardingDecisionOutcome.AutoPromote,
        Label = OnboardingDecisionLabels.Normal,
        Probabilities = new Dictionary<string, double> { [OnboardingDecisionLabels.Normal] = 0.9 },
        Threshold = 0.8,
        EvaluatedAt = DateTime.UtcNow
    };

    private sealed class PromotionDouble
    {
        public int Promotions { get; private set; }

        public int Removals { get; private set; }

        public bool FailPromotion { get; init; }

        public Task PromoteAsync()
        {
            Promotions++;
            if (FailPromotion)
            {
                throw new InvalidOperationException("role update failed");
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync()
        {
            Removals++;
            return Task.CompletedTask;
        }
    }
}

public sealed class SystemOneProbeTests
{
    [Fact]
    public async Task UnconfiguredEndpoint_DoesNotSendARequest()
    {
        ProbeClient client = new();
        SystemOneProbe probe = new(SystemOneSettings.Create(new SystemOneConfig()), client);

        SystemOneProbeReport report = await probe.ProbeAsync();

        report.Status.Should().Be(SystemOneProbeStatus.NotConfigured);
        client.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SuccessfulRoundTrip_ReportsTheVerdict()
    {
        ProbeClient client = new();
        SystemOneProbe probe = new(Configured("super-secret-token"), client);

        SystemOneProbeReport report = await probe.ProbeAsync();

        report.Status.Should().Be(SystemOneProbeStatus.Succeeded);
        report.Label.Should().Be(OnboardingDecisionLabels.Normal);
        report.Endpoint.Should().Be("http://127.0.0.1:9");
        report.Endpoint.Should().NotContain("@");
        report.UsedAuthorization.Should().BeTrue();
        report.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        client.Calls.Should().Be(1);
        client.LastSubmission!.Answers.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConnectionFailure_RedactsTheToken()
    {
        const string token = "super-secret-token";
        ProbeClient client = new() { Error = new HttpRequestException($"unauthorized {token}") };
        SystemOneProbe probe = new(Configured(token), client);

        SystemOneProbeReport report = await probe.ProbeAsync();

        report.Status.Should().Be(SystemOneProbeStatus.Failed);
        report.Failure.Should().Contain(nameof(HttpRequestException));
        report.Failure.Should().NotContain(token);
        report.Failure.Should().Contain("***");
    }

    [Fact]
    public async Task EndpointReport_OmitsUserInfo()
    {
        SystemOneSettings settings = SystemOneSettings.Create(new SystemOneConfig
        {
            BaseUrl = "http://user:pass@127.0.0.1:9",
            TimeoutSeconds = 2
        });
        SystemOneProbe probe = new(settings, new ProbeClient());

        SystemOneProbeReport report = await probe.ProbeAsync();

        report.Endpoint.Should().Be("http://127.0.0.1:9");
    }

    private static SystemOneSettings Configured(string? token) =>
        SystemOneSettings.Create(new SystemOneConfig
        {
            BaseUrl = "http://127.0.0.1:9",
            ApiToken = token,
            TimeoutSeconds = 2
        });

    private sealed class ProbeClient : IOnboardingDecisionClient
    {
        public int Calls { get; private set; }

        public Exception? Error { get; init; }

        public OnboardingSubmission? LastSubmission { get; private set; }

        public Task<SystemOneClassification> ClassifyAsync(
            OnboardingSubmission submission,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSubmission = submission;
            if (Error is not null)
            {
                throw Error;
            }

            return Task.FromResult(new SystemOneClassification
            {
                Model = "loaded",
                Label = OnboardingDecisionLabels.Normal,
                Confidence = 0.5,
                Probabilities = new Dictionary<string, double>
                {
                    [OnboardingDecisionLabels.Normal] = 0.7,
                    [OnboardingDecisionLabels.Bot] = 0.2,
                    [OnboardingDecisionLabels.Reject] = 0.1
                }
            });
        }
    }
}

public sealed class SystemOneSettingsTests
{
    [Fact]
    public void EmptyAddress_LeavesTheIntegrationUnconfigured()
    {
        SystemOneSettings settings = SystemOneSettings.Create(new SystemOneConfig { ApiToken = "   " });

        settings.IsConfigured.Should().BeFalse();
        settings.ApiToken.Should().BeNull();
        settings.Model.Should().Be("loaded");
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    public void InvalidTimeout_FailsStartup(string raw)
    {
        SystemOneConfig config = new() { TimeoutSeconds = int.Parse(raw == "" ? "0" : raw) };
        Action create = () => SystemOneSettings.Create(config);
        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RelativeAddress_FailsStartup()
    {
        Action create = () => SystemOneSettings.Create(new SystemOneConfig { BaseUrl = "/v1/systemone" });
        create.Should().Throw<InvalidOperationException>();
    }
}

public sealed class GuildConfigAutomationMappingTests
{
    [Fact]
    public void LegacyDocument_DefaultsScreeningToOff()
    {
        GuildConfigEntity entity = GuildConfigEntity.FromGuildConfig(new GuildConfig
        {
            GuildId = 1,
            Questionnaires = new Dictionary<string, Questionnaire>
            {
                [QuestionnaireKeys.Member] = new()
                {
                    Id = "newbie",
                    Name = "On-boarding",
                    SubmissionChannelId = 5,
                    Automation = new QuestionnaireAutomation
                    {
                        Enabled = true,
                        Context = "temporary",
                        AutoPromoteNormalProbability = 0.2
                    }
                }
            }
        });

        BsonDocument document = entity.ToBsonDocument();
        document["Questionnaires"]["Member"].AsBsonDocument.Remove("Automation");
        GuildConfigEntity restored = BsonSerializer.Deserialize<GuildConfigEntity>(document);
        Questionnaire questionnaire = restored.ToGuildConfig().Questionnaires[QuestionnaireKeys.Member];

        questionnaire.Automation.Enabled.Should().BeFalse();
        questionnaire.Automation.AutoPromoteNormalProbability.Should().Be(0.80);
        questionnaire.Automation.Context.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_PreservesAutomationPolicy()
    {
        GuildConfig config = new()
        {
            GuildId = 7,
            Questionnaires = new Dictionary<string, Questionnaire>
            {
                [QuestionnaireKeys.Member] = new()
                {
                    Id = "newbie",
                    Name = "On-boarding",
                    Automation = new QuestionnaireAutomation
                    {
                        Enabled = true,
                        Context = "DsHidMini",
                        AutoPromoteNormalProbability = 0.91
                    }
                }
            }
        };

        GuildConfig restored = GuildConfigEntity.FromGuildConfig(config).ToGuildConfig();
        QuestionnaireAutomation automation = restored.Questionnaires[QuestionnaireKeys.Member].Automation;

        automation.Enabled.Should().BeTrue();
        automation.Context.Should().Be("DsHidMini");
        automation.AutoPromoteNormalProbability.Should().Be(0.91);
    }
}
