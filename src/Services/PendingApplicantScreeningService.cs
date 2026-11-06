using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Services;

internal interface IPendingApplicantScreeningService
{
    Task ScreenAllAsync(CancellationToken ct = default);
    Task ScreenGuildAsync(ulong guildId, CancellationToken ct = default);
    Task<OnboardingAutomationEffect> ScreenMemberAsync(string memberId, CancellationToken ct = default);
}

internal sealed class PendingApplicantScreeningService(
    DB db, IGuildConfigService configs, IDiscordReadinessService readiness, SystemOneSettings settings,
    IOnboardingDecisionService decisions, IMemberPromotionService promotions,
    IOnboardingAutomationExecutor automation, IMemberRoleClient roles, IApplicantSubmissionClient submissions,
    ILogger<PendingApplicantScreeningService> logger) : IPendingApplicantScreeningService
{
    public async Task ScreenAllAsync(CancellationToken ct = default)
    {
        foreach (GuildConfig config in await configs.GetAllAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            await ScreenGuildAsync(config.GuildId, ct);
        }
    }

    public async Task ScreenGuildAsync(ulong guildId, CancellationToken ct = default)
    {
        await SingleFlight.RunAsync($"pending-applicant-screening:{guildId}", async () =>
        {
            try
            {
                GuildConfig? config = await configs.GetFreshAsync(guildId, ct);
                if (!Enabled(config) || !settings.IsConfigured || !readiness.IsGuildReady(guildId) ||
                    !roles.IsGuildAvailable(guildId))
                {
                    logger.LogInformation("Pending applicant screening skipped for guild {GuildId}: {Reason}",
                        guildId, !Enabled(config) ? "screening disabled" : !settings.IsConfigured
                            ? "endpoint not configured" : "guild not ready");
                    return;
                }

                List<GuildMember> waiting = await db.Find<GuildMember>()
                    .Match(m => m.GuildId == guildId && m.Status == MemberStatus.QuestionnaireSubmitted).ExecuteAsync(ct);
                int promoted = 0;
                foreach (GuildMember snapshot in waiting.Where(m =>
                             PendingApplicantScreeningPolicy.NeedsScreening(m.Status, m.Application)))
                {
                    ct.ThrowIfCancellationRequested();
                    if (await ScreenMemberAsync(snapshot.ID, ct) == OnboardingAutomationEffect.Promoted)
                    {
                        promoted++;
                    }
                }

                logger.LogInformation("Pending applicant screening for guild {GuildId} complete: {Promoted} promoted",
                    guildId, promoted);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pending applicant screening failed for guild {GuildId}", guildId);
            }
        }, logger);
    }

    public async Task<OnboardingAutomationEffect> ScreenMemberAsync(string memberId, CancellationToken ct = default)
    {
        OnboardingAutomationEffect effect = OnboardingAutomationEffect.Held;
        await SingleFlight.RunAsync($"applicant-screening:{memberId}", async () =>
        {
            try
            {
                effect = await ScreenMemberCoreAsync(memberId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Screening deferred for applicant {MemberId}; manual review remains available", memberId);
            }
        }, logger);
        return effect;
    }

    private async Task<OnboardingAutomationEffect> ScreenMemberCoreAsync(string memberId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        GuildMember? applicant = await db.Find<GuildMember>().OneAsync(memberId, ct);
        if (applicant is null || !PendingApplicantScreeningPolicy.NeedsScreening(applicant.Status, applicant.Application))
        {
            return OnboardingAutomationEffect.Held;
        }

        GuildConfig? config = await configs.GetFreshAsync(applicant.GuildId, ct);
        if (!Enabled(config) || !settings.IsConfigured || !roles.IsGuildAvailable(applicant.GuildId))
        {
            logger.LogInformation("Screening skipped for {MemberId}: {Reason}", memberId,
                !Enabled(config) ? "questionnaire screening disabled" : !settings.IsConfigured
                    ? "System One endpoint not configured" : "guild unavailable");
            return OnboardingAutomationEffect.Held;
        }

        Questionnaire questionnaire = config!.Questionnaires[QuestionnaireKeys.Member];
        ApplicantSubmission? submission = await submissions.RecoverAsync(applicant, questionnaire, ct);
        if (submission is null)
        {
            logger.LogWarning("No complete, unambiguous submission recovered for {MemberId}; manual review required", memberId);
            return OnboardingAutomationEffect.Held;
        }

        if (!(await roles.GetAsync(applicant.GuildId, applicant.MemberId, ct)).IsApplicant(config))
        {
            logger.LogInformation("Screening skipped for {MemberId}: current Discord roles are not an applicant", memberId);
            return OnboardingAutomationEffect.Held;
        }

        string? promotionProblem = await roles.CheckPromotionAsync(config, ct);
        OnboardingDecisionResult decision = promotionProblem is null
            ? await decisions.EvaluateAsync(questionnaire, submission.Answers, applicant.GuildId, applicant.MemberId, ct)
            : OnboardingDecisionPolicy.Unavailable(DateTime.UtcNow, promotionProblem);
        GuildConfig? latest = await configs.GetFreshAsync(applicant.GuildId, ct);
        if (!Enabled(latest) || latest!.ConfigVersion != config.ConfigVersion ||
            !(await roles.GetAsync(applicant.GuildId, applicant.MemberId, ct)).IsApplicant(latest))
        {
            logger.LogInformation("Configuration or roles changed during screening of {MemberId}; deferring", memberId);
            return OnboardingAutomationEffect.Held;
        }

        bool willPromote = decision.Outcome == OnboardingDecisionOutcome.AutoPromote;
        if (!await TryRecordAsync(db, applicant, decision.ToRecord(willPromote, false),
                submission.ChannelId, submission.MessageId, ct))
        {
            logger.LogInformation("Application changed during screening of {MemberId}; result discarded", memberId);
            return OnboardingAutomationEffect.Held;
        }

        OnboardingAutomationEffect effect = await automation.ExecuteAsync(decision,
            new OnboardingAutomationState(false, MemberStatus.QuestionnaireSubmitted, true),
            () => promotions.PromoteAsync(applicant, latest, null, OnboardingDecisionLabels.AutoPromoteReason, ct), ct);
        OnboardingDecisionRecord completed = decision.ToRecord(willPromote, effect == OnboardingAutomationEffect.Promoted);
        if (effect == OnboardingAutomationEffect.PromotionFailed)
        {
            completed.Failure = "Automatic promotion failed; moderator action required. Check bot permissions and role hierarchy.";
        }

        // The role-removal gateway event can already have deleted Application. Keep the audit result independently.
        await db.Update<GuildMember>().MatchID(memberId)
            .Match(f => f.Eq(m => m.LastOnboardingDecision!.EvaluatedAt, decision.EvaluatedAt))
            .Modify(m => m.LastOnboardingDecision, completed).ExecuteAsync(ct);
        await applicant.SetApplicationDecisionAsync(db, completed);
        logger.LogInformation("Screening completed for {MemberId}: outcome={Outcome} effect={Effect} normal={Normal} threshold={Threshold}",
            memberId, decision.Outcome, effect, decision.Probabilities.GetValueOrDefault("normal"), decision.Threshold);
        try
        {
            await submissions.RefreshAsync(applicant, submission, decision, effect, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Screening result persisted but moderator message refresh failed for {MemberId}", memberId);
        }

        return effect;
    }

    private static bool Enabled(GuildConfig? config) => config is { EnableOnboardingWorkflow: true } &&
        config.Questionnaires.TryGetValue(QuestionnaireKeys.Member, out Questionnaire? questionnaire) &&
        questionnaire.Automation.Enabled;

    internal static async Task<bool> TryRecordAsync(DB db, GuildMember snapshot, OnboardingDecisionRecord decision,
        ulong channelId, ulong messageId, CancellationToken ct = default)
    {
        if (!PendingApplicantScreeningPolicy.NeedsScreening(snapshot.Status, snapshot.Application))
        {
            return false;
        }

        StrangerApplicationEmbed application = snapshot.Application!;
        var result = await db.Update<GuildMember>()
            .MatchID(snapshot.ID)
            .Match(f => f.Eq(m => m.Status, MemberStatus.QuestionnaireSubmitted) &
                        f.Eq(m => m.Application!.ID, application.ID) &
                        f.Eq(m => m.Application!.QuestionnaireSubmittedAt, application.QuestionnaireSubmittedAt) &
                        f.Eq(m => m.Application!.Decision, application.Decision))
            .Modify(m => m.Application!.Decision, decision)
            .Modify(m => m.LastOnboardingDecision, decision)
            .Modify(m => m.Application!.SubmissionChannelId, (ulong?)channelId)
            .Modify(m => m.Application!.SubmissionMessageId, (ulong?)messageId)
            .ExecuteAsync(ct);
        return result.ModifiedCount == 1;
    }

}
