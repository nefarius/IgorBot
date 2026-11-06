using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Services;

using Microsoft.Extensions.Logging.Abstractions;

using MongoDB.Entities;

namespace IgorBot.Tests.Integration;

// Real MongoDB, config service/cache, System One HTTP serialization/deserialization, decision policy,
// promotion service and screening workflow. Only Discord's network boundary and inference response are doubles.
[Xunit.Collection("Mongo")]
public sealed class ScreeningPipelineTests(MongoFixture mongo)
{
    [Fact]
    public async Task FreshSubmission_UsesNewlyEnabledConfigAndPromotesExactlyOnce()
    {
        using Pipeline pipeline = await CreateAsync(enabled: false);
        (await pipeline.Configs.GetAsync(1))!.Questionnaires[QuestionnaireKeys.Member].Automation.Enabled.Should().BeFalse();
        await pipeline.ConfigureAsync(true);
        GuildMember applicant = await pipeline.LoadAsync();

        (await applicant.SetQuestionnaireSubmittedAsync(pipeline.Db, DateTime.UtcNow, null, 7, 8)).Should().BeTrue();
        (await pipeline.Screening.ScreenMemberAsync(applicant.ID)).Should().Be(OnboardingAutomationEffect.Promoted);
        await pipeline.Screening.ScreenGuildAsync(1);
        await pipeline.Screening.ScreenMemberAsync(applicant.ID);

        GuildMember saved = await pipeline.LoadAsync();
        saved.Status.Should().Be(MemberStatus.FullMember);
        saved.StatusHistory.Select(e => e.To).Should().Equal(MemberStatus.QuestionnaireSubmitted, MemberStatus.FullMember);
        saved.LastOnboardingDecision!.AutoPromoted.Should().BeTrue();
        pipeline.Roles.Changes.Should().Equal((20UL, true), (10UL, false));
        pipeline.Roles.Welcomes.Should().Be(1);
        pipeline.Submissions.Effects.Should().Equal(OnboardingAutomationEffect.Promoted);
        pipeline.Http.Calls.Should().Be(1);
        pipeline.Http.Path.Should().Be("/v1/systemone");
        pipeline.Http.Authorization.Should().Be("Bearer test-token");
        using JsonDocument request = JsonDocument.Parse(pipeline.Http.Body);
        request.RootElement.GetProperty("questions").GetProperty("verdict").GetProperty("type").GetString().Should().Be("choice");
        request.RootElement.GetProperty("state").GetProperty("answers")[4].GetProperty("answer").GetString()
            .Should().Be("because I wanted to report a visual bug and GitHub issues for DsHidMini is locked");
        pipeline.Http.Body.Should().NotContain("724010072065441842");
    }

    [Theory]
    [InlineData("normal", 0.6, 0.2, 0.2)]
    [InlineData("bot", 0.05, 0.9, 0.05)]
    [InlineData("reject", 0.05, 0.05, 0.9)]
    public async Task ManualReviewDecision_IsPersistedWithoutChangingRolesOrRepeatedInference(
        string label, double normal, double bot, double reject)
    {
        using Pipeline pipeline = await CreateAsync();
        pipeline.Http.Json = Verdict(label, normal, bot, reject);
        await pipeline.SubmitAsync();

        (await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId)).Should().Be(OnboardingAutomationEffect.Held);
        await pipeline.Screening.ScreenAllAsync();

        (await pipeline.LoadAsync()).Application!.Decision!.Outcome.Should().Be("ManualReview");
        pipeline.Roles.Changes.Should().BeEmpty();
        pipeline.Roles.Welcomes.Should().Be(0);
        pipeline.Http.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("{\"model\":\"loaded\",\"answers\":{}}")]
    [InlineData("{\"model\":\"loaded\",\"answers\":{\"verdict\":{\"type\":\"choice\",\"choice\":\"normal\",\"probabilities\":{\"normal\":1}}}}")]
    [InlineData("not-json")]
    public async Task InvalidHttpResponse_FallsBackToManualReviewAndRecoversOnNextJob(string json)
    {
        using Pipeline pipeline = await CreateAsync();
        pipeline.Http.Json = json;
        await pipeline.SubmitAsync();
        await pipeline.Screening.ScreenAllAsync();

        (await pipeline.LoadAsync()).Application!.Decision!.Outcome.Should().Be("Unavailable");
        pipeline.Roles.Changes.Should().BeEmpty();
        pipeline.Http.Json = Verdict();
        await pipeline.Screening.ScreenAllAsync();
        (await pipeline.LoadAsync()).Status.Should().Be(MemberStatus.FullMember);
        pipeline.Http.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData("normal", 0.9, 0.9, 0.1, 0.8)]
    [InlineData("normal", 0.2, 0.7, 0.1, 0.8)]
    [InlineData("normal", 0.9, 0.05, 0.05, 2)]
    public async Task InvalidDistributionOrConfidence_NeverAutoApproves(
        string label, double normal, double bot, double reject, double confidence)
    {
        using Pipeline pipeline = await CreateAsync();
        pipeline.Http.Json = Verdict(label, normal, bot, reject, confidence);
        await pipeline.SubmitAsync();
        await pipeline.Screening.ScreenAllAsync();
        (await pipeline.LoadAsync()).Application!.Decision!.Outcome.Should().Be("Unavailable");
        pipeline.Roles.Changes.Should().BeEmpty();
        (await new SystemOneProbe(pipeline.Settings, pipeline.Client!).ProbeAsync()).Status.Should().Be(SystemOneProbeStatus.Failed);
    }

    [Fact]
    public async Task StartupBeforeReadinessOrEndpointRecovery_IsPickedUpByNextScan()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Readiness.Reset();
        await pipeline.Screening.ScreenAllAsync();
        pipeline.Http.Calls.Should().Be(0);
        pipeline.Readiness.MarkGuildAvailable(1);
        pipeline.Http.Status = HttpStatusCode.ServiceUnavailable;
        await pipeline.Screening.ScreenAllAsync();
        (await pipeline.LoadAsync()).Application!.Decision!.Outcome.Should().Be("Unavailable");
        pipeline.Http.Status = HttpStatusCode.OK;
        await pipeline.Screening.ScreenAllAsync();
        (await pipeline.LoadAsync()).Status.Should().Be(MemberStatus.FullMember);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task DisabledQuestionnaireOrMissingEndpoint_DoesNotSendInference(bool enabled, bool configured)
    {
        using Pipeline pipeline = await CreateAsync(enabled, configured);
        await pipeline.SubmitAsync();
        await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId);
        await pipeline.Screening.ScreenAllAsync();
        pipeline.Http.Calls.Should().Be(0);
        pipeline.Roles.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigurationDisabledDuringInference_DiscardsDecision()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Http.OnRequest = () => pipeline.ConfigureAsync(false);
        await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId);
        pipeline.Roles.Changes.Should().BeEmpty();
        (await pipeline.LoadAsync()).Application!.Decision.Should().BeNull();
    }

    [Fact]
    public async Task MissingManageRoles_DefersBeforeInferenceAndRetriesAfterPermissionsAreFixed()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Roles.PromotionProblem = "Bot needs Manage Roles";
        await pipeline.Screening.ScreenAllAsync();
        pipeline.Http.Calls.Should().Be(0);
        GuildMember deferred = await pipeline.LoadAsync();
        deferred.LastOnboardingDecision!.Failure.Should().Contain("Manage Roles");
        deferred.LastOnboardingDecision.AutomationAttempted.Should().BeFalse();
        pipeline.Roles.PromotionProblem = null;
        await pipeline.Screening.ScreenAllAsync();
        (await pipeline.LoadAsync()).Status.Should().Be(MemberStatus.FullMember);
    }

    [Fact]
    public async Task InferenceTimeout_StaysPendingAndCanBeRetried()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Http.Delay = TimeSpan.FromSeconds(10);
        await pipeline.Screening.ScreenAllAsync();
        (await pipeline.LoadAsync()).Application!.Decision!.Outcome.Should().Be("Unavailable");
        pipeline.Roles.Changes.Should().BeEmpty();
        pipeline.Http.Delay = TimeSpan.Zero;
        await pipeline.Screening.ScreenAllAsync();
        (await pipeline.LoadAsync()).Status.Should().Be(MemberStatus.FullMember);
    }

    [Fact]
    public async Task RejoinDuringInference_RejectsOldApplicationResult()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Http.OnRequest = async () =>
        {
            GuildMember member = await pipeline.LoadAsync();
            member.Reset();
            member.JoinedAt = DateTime.UtcNow.AddSeconds(1);
            await pipeline.Db.SaveAsync(member);
        };
        await pipeline.Screening.ScreenAllAsync();
        pipeline.Roles.Changes.Should().BeEmpty();
        GuildMember rejoined = await pipeline.LoadAsync();
        rejoined.Status.Should().Be(MemberStatus.New);
        rejoined.LastOnboardingDecision.Should().BeNull();
    }

    [Fact]
    public async Task StaleSameStatusTransition_DoesNotReportSuccessAfterDeparture()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        GuildMember stale = await pipeline.LoadAsync();
        await (await pipeline.LoadAsync()).TransitionToAsync(pipeline.Db, MemberStatus.LeftVoluntarily);
        (await stale.TryTransitionAsync(pipeline.Db, MemberStatus.QuestionnaireSubmitted)).Should().BeFalse();
    }

    [Fact]
    public async Task RolesChangedDuringInference_DiscardsDecision()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Http.OnRequest = () => { pipeline.Roles.Current.Clear(); return Task.CompletedTask; };
        await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId);
        pipeline.Roles.Changes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MemberStatus.LeftVoluntarily)]
    [InlineData(MemberStatus.BannedByModerator)]
    [InlineData(MemberStatus.FullMember)]
    public async Task LifecycleChangedDuringInference_DoesNotPromote(MemberStatus next)
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Http.OnRequest = async () => await (await pipeline.LoadAsync()).TransitionToAsync(pipeline.Db, next);
        await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId);
        (await pipeline.LoadAsync()).Status.Should().Be(next);
        pipeline.Roles.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentLiveAndBacklogRuns_InferAndPromoteOnlyOnce()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.Http.OnRequest = async () => { started.TrySetResult(); await release.Task; };
        Task<OnboardingAutomationEffect> live = pipeline.Screening.ScreenMemberAsync(pipeline.MemberId);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await pipeline.Screening.ScreenAllAsync();
        }
        finally
        {
            release.TrySetResult();
        }

        (await live).Should().Be(OnboardingAutomationEffect.Promoted);
        pipeline.Http.Calls.Should().Be(1);
        pipeline.Roles.Changes.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(10UL)]
    [InlineData(20UL)]
    public async Task RolePermissionFailure_IsRecordedAndDoesNotClaimAcceptanceOrRepeat(ulong failingRole)
    {
        using Pipeline pipeline = await CreateAsync();
        pipeline.Roles.FailingRole = failingRole;
        await pipeline.SubmitAsync();
        (await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId)).Should().Be(OnboardingAutomationEffect.PromotionFailed);
        await pipeline.Screening.ScreenAllAsync();
        GuildMember saved = await pipeline.LoadAsync();
        saved.Status.Should().Be(MemberStatus.QuestionnaireSubmitted);
        saved.LastOnboardingDecision!.AutoPromoted.Should().BeFalse();
        saved.LastOnboardingDecision.Failure.Should().Contain("permissions");
        pipeline.Roles.Welcomes.Should().Be(0);
        pipeline.Http.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GatewayCleanupDuringPromotion_PreservesAuditAndUpdatesSubmission()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Roles.AfterRemove = async () =>
        {
            GuildMember member = await pipeline.LoadAsync();
            await member.TransitionToAsync(pipeline.Db, MemberStatus.FullMember);
            await pipeline.Db.Update<GuildMember>().MatchID(member.ID)
                .Modify(m => m.Application, (StrangerApplicationEmbed?)null).ExecuteAsync();
        };

        (await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId)).Should().Be(OnboardingAutomationEffect.Promoted);
        GuildMember saved = await pipeline.LoadAsync();
        saved.Application.Should().BeNull();
        saved.LastOnboardingDecision!.AutoPromoted.Should().BeTrue();
        pipeline.Submissions.Effects.Should().Equal(OnboardingAutomationEffect.Promoted);
    }

    [Fact]
    public async Task MissingSubmission_DoesNotInferAndNotificationFailureDoesNotUndoPromotion()
    {
        using Pipeline pipeline = await CreateAsync();
        await pipeline.SubmitAsync();
        pipeline.Submissions.Missing = true;
        await pipeline.Screening.ScreenAllAsync();
        pipeline.Http.Calls.Should().Be(0);
        pipeline.Submissions.Missing = false;
        pipeline.Submissions.FailRefresh = true;
        (await pipeline.Screening.ScreenMemberAsync(pipeline.MemberId)).Should().Be(OnboardingAutomationEffect.Promoted);
        (await pipeline.LoadAsync()).LastOnboardingDecision!.AutoPromoted.Should().BeTrue();
    }

    [Fact]
    public async Task FailedPostOrStaleSubmission_DoesNotMarkQuestionnaireSubmitted()
    {
        using Pipeline pipeline = await CreateAsync();
        GuildMember original = await pipeline.LoadAsync();
        (await original.SetQuestionnaireSubmittedAsync(pipeline.Db, DateTime.UtcNow, null)).Should().BeFalse();
        (await pipeline.LoadAsync()).Status.Should().Be(MemberStatus.Onboarding);
        await (await pipeline.LoadAsync()).TransitionToAsync(pipeline.Db, MemberStatus.LeftVoluntarily);
        (await original.SetQuestionnaireSubmittedAsync(pipeline.Db, DateTime.UtcNow, null, 7, 8)).Should().BeFalse();
        (await pipeline.LoadAsync()).Application!.QuestionnaireSubmittedAt.Should().BeNull();
    }

    private async Task<Pipeline> CreateAsync(bool enabled = true, bool configured = true)
    {
        DB db = await mongo.CreateDatabaseAsync($"igor-screening-pipeline-{Guid.NewGuid():N}");
        Pipeline pipeline = new(db, configured);
        await pipeline.ConfigureAsync(enabled);
        GuildMember member = new()
        {
            ID = pipeline.MemberId, GuildId = 1, MemberId = 724010072065441842,
            Member = "kokasgui", Mention = "<@724010072065441842>", Status = MemberStatus.Onboarding
        };
        await db.SaveAsync(member);
        await db.Update<GuildMember>().MatchID(member.ID).Modify(m => m.Application,
            new StrangerApplicationEmbed { ID = "1-2-3", GuildId = 1, ChannelId = 2, MessageId = 3 }).ExecuteAsync();
        return pipeline;
    }

    private static string Verdict(string label = "normal", double normal = 0.9, double bot = 0.05,
        double reject = 0.05, double confidence = 0.8) => JsonSerializer.Serialize(new
    {
        model = "loaded", answers = new { verdict = new
        {
            choice = label, probabilities = new { normal, bot, reject }, confidence, type = "choice"
        } }
    });

    private sealed class Pipeline : IDisposable
    {
        public Pipeline(DB db, bool configured)
        {
            Db = db;
            Configs = new CachedGuildConfigService(new GuildConfigService(db), NullLogger<CachedGuildConfigService>.Instance);
            Settings = SystemOneSettings.Create(new SystemOneConfig
            {
                BaseUrl = configured ? "http://127.0.0.1:9" : "", ApiToken = "test-token", TimeoutSeconds = 1
            });
            Client = configured ? new OllamaSharpOnboardingDecisionClient(Settings, Http) : null;
            Readiness.MarkGuildAvailable(1);
            Screening = new PendingApplicantScreeningService(db, Configs, Readiness, Settings,
                new OnboardingDecisionService(Settings, (IOnboardingDecisionClient?)Client ?? new UnconfiguredOnboardingDecisionClient(),
                    NullLogger<OnboardingDecisionService>.Instance),
                new MemberPromotionService(db, Roles, NullLogger<MemberPromotionService>.Instance),
                new OnboardingAutomationExecutor(NullLogger<OnboardingAutomationExecutor>.Instance), Roles, Submissions,
                NullLogger<PendingApplicantScreeningService>.Instance);
        }

        public string MemberId => "1-724010072065441842";
        public DB Db { get; }
        public IGuildConfigService Configs { get; }
        public SystemOneSettings Settings { get; }
        public OllamaSharpOnboardingDecisionClient? Client { get; }
        public DecisionHttp Http { get; } = new();
        public RoleClient Roles { get; } = new();
        public SubmissionClient Submissions { get; } = new();
        public DiscordReadinessService Readiness { get; } = new();
        public PendingApplicantScreeningService Screening { get; }
        public async Task<GuildMember> LoadAsync() => (await Db.Find<GuildMember>().OneAsync(MemberId))!;
        public async Task SubmitAsync() => (await (await LoadAsync()).SetQuestionnaireSubmittedAsync(Db,
            DateTime.UtcNow, null, 7, 8)).Should().BeTrue();

        public async Task ConfigureAsync(bool enabled)
        {
            GuildConfig config = await Configs.GetFreshAsync(1) ?? new GuildConfig
            {
                GuildId = 1, StrangerRoleId = 10, MemberRoleId = 20,
                Questionnaires = new Dictionary<string, Questionnaire>
                {
                    [QuestionnaireKeys.Member] = new()
                    {
                        Name = "On-boarding", Id = "member", SubmissionChannelId = 7,
                        Questions = Answers.Select(a => new Question { Content = a.Question }).ToList()
                    }
                }
            };
            config.Questionnaires[QuestionnaireKeys.Member].Automation.Enabled = enabled;
            await Configs.SaveAsync(config);
        }

        public void Dispose() => Client?.Dispose();
    }

    private static readonly OnboardingAnswer[] Answers =
    [
        new("Have you read, understood and acknowledged our very simple server rules?", "Yup"),
        new("What is your mother tongue (native language)?", "portuguese"),
        new("Are you fluent in English without a machine translator (like Google Translate)?", "yup"),
        new("How/Where did you hear about this Discord server?", "I saw the link in the docs"),
        new("Why would you like to join?", "because I wanted to report a visual bug and GitHub issues for DsHidMini is locked")
    ];

    private sealed class SubmissionClient : IApplicantSubmissionClient
    {
        public bool Missing { get; set; }
        public bool FailRefresh { get; set; }
        public List<OnboardingAutomationEffect> Effects { get; } = [];
        public Task<ApplicantSubmission?> RecoverAsync(GuildMember applicant, Questionnaire questionnaire, CancellationToken ct) =>
            Task.FromResult(Missing ? null : new ApplicantSubmission(7, 8,
                PendingApplicantScreeningPolicy.ReadAnswers(questionnaire, Answers)!));
        public Task RefreshAsync(GuildMember applicant, ApplicantSubmission submission, OnboardingDecisionResult decision,
            OnboardingAutomationEffect effect, CancellationToken ct)
        {
            if (FailRefresh) throw new InvalidOperationException("Discord message edit failed");
            Effects.Add(effect);
            return Task.CompletedTask;
        }
    }

    private sealed class RoleClient : IMemberRoleClient
    {
        public HashSet<ulong> Current { get; } = [10];
        public List<(ulong Role, bool Grant)> Changes { get; } = [];
        public ulong? FailingRole { get; set; }
        public string? PromotionProblem { get; set; }
        public Func<Task>? AfterRemove { get; set; }
        public int Welcomes { get; private set; }
        public bool IsGuildAvailable(ulong guildId) => true;
        public Task<string?> CheckPromotionAsync(GuildConfig config, CancellationToken ct) => Task.FromResult(PromotionProblem);
        public Task<MemberRoleSnapshot> GetAsync(ulong guildId, ulong memberId, CancellationToken ct) =>
            Task.FromResult(new MemberRoleSnapshot(false, Current.ToArray()));
        public async Task SetRoleAsync(ulong guildId, ulong memberId, ulong roleId, bool grant, CancellationToken ct)
        {
            if (roleId == FailingRole) throw new UnauthorizedAccessException("Missing ManageRoles or role hierarchy");
            Changes.Add((roleId, grant));
            if (grant) Current.Add(roleId); else Current.Remove(roleId);
            if (!grant && AfterRemove is not null) await AfterRemove();
        }
        public Task WelcomeAsync(GuildMember entry, GuildConfig config, CancellationToken ct)
        {
            Welcomes++;
            return Task.CompletedTask;
        }
    }

    private sealed class DecisionHttp : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Body { get; private set; } = "";
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string Json { get; set; } = Verdict();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Func<Task>? OnRequest { get; set; }
        public TimeSpan Delay { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            Body = await request.Content!.ReadAsStringAsync(ct);
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            if (OnRequest is not null) await OnRequest();
            return new HttpResponseMessage(Status) { Content = new StringContent(Json, Encoding.UTF8, "application/json") };
        }
    }
}
