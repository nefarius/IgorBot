using IgorBot.Core;
using IgorBot.Schema;
using IgorBot.Util;

using MongoDB.Entities;

namespace IgorBot.Services;

internal sealed class MemberPromotionService(DB db, IMemberRoleClient roles, ILogger<MemberPromotionService> logger)
    : IMemberPromotionService
{
    public async Task PromoteAsync(GuildMember entry, GuildConfig guildConfig, ulong? actorId, string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!await SingleFlight.RunAsync($"member-promotion:{entry.ID}",
                () => PromoteCoreAsync(entry, guildConfig, actorId, reason, cancellationToken), logger))
        {
            throw new InvalidOperationException("Another promotion for this member is in progress.");
        }
    }

    private async Task PromoteCoreAsync(GuildMember entry, GuildConfig guildConfig, ulong? actorId, string? reason,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GuildMember? current = await db.Find<GuildMember>().OneAsync(entry.ID, cancellationToken);
        if (current is not null && current.JoinedAt != entry.JoinedAt)
        {
            throw new InvalidOperationException("Member rejoined after this application was loaded.");
        }
        if (current?.Status == MemberStatus.FullMember)
        {
            MemberRoleSnapshot existing = await roles.GetAsync(entry.GuildId, entry.MemberId, cancellationToken);
            if (existing.RoleIds.Contains(guildConfig.MemberRoleId) && !existing.RoleIds.Contains(guildConfig.StrangerRoleId))
            {
                return;
            }

            throw new InvalidOperationException("Stored FullMember status does not match Discord roles; moderator repair required.");
        }

        if (current is null || current.JoinedAt != entry.JoinedAt ||
            MemberTransitionPolicy.AllowedFrom(MemberStatus.FullMember)?.Contains(current.Status) != true ||
            (reason == OnboardingDecisionLabels.AutoPromoteReason &&
             (current.Status != MemberStatus.QuestionnaireSubmitted || current.Application?.ID != entry.Application?.ID ||
              current.Application?.QuestionnaireSubmittedAt != entry.Application?.QuestionnaireSubmittedAt)))
        {
            throw new InvalidOperationException("Application changed before promotion.");
        }

        if (guildConfig.MemberRoleId == 0 || guildConfig.StrangerRoleId == 0 ||
            guildConfig.MemberRoleId == guildConfig.StrangerRoleId)
        {
            throw new InvalidOperationException("Distinct stranger and member roles must be configured.");
        }

        await roles.SetRoleAsync(entry.GuildId, entry.MemberId, guildConfig.MemberRoleId, true, cancellationToken);
        current = await db.Find<GuildMember>().OneAsync(entry.ID, cancellationToken);
        if (current is null || current.JoinedAt != entry.JoinedAt ||
            MemberTransitionPolicy.AllowedFrom(MemberStatus.FullMember)?.Contains(current.Status) != true)
        {
            throw new InvalidOperationException("Member lifecycle changed while granting the member role.");
        }
        await roles.SetRoleAsync(entry.GuildId, entry.MemberId, guildConfig.StrangerRoleId, false, cancellationToken);

        MemberRoleSnapshot verified = await roles.GetAsync(entry.GuildId, entry.MemberId, cancellationToken);
        if (!verified.RoleIds.Contains(guildConfig.MemberRoleId) || verified.RoleIds.Contains(guildConfig.StrangerRoleId))
        {
            throw new InvalidOperationException("Discord did not confirm the promotion role changes.");
        }

        bool recorded = false;
        for (int attempt = 0; attempt < 2 && !recorded; attempt++)
        {
            current = await db.Find<GuildMember>().OneAsync(entry.ID, cancellationToken);
            if (current?.Status == MemberStatus.FullMember)
            {
                if (current.JoinedAt != entry.JoinedAt)
                {
                    break;
                }
                recorded = true; // Gateway events may have recorded the same role change.
                break;
            }

            if (current is null || MemberTransitionPolicy.AllowedFrom(MemberStatus.FullMember)?.Contains(current.Status) != true)
            {
                break;
            }

            if (current.JoinedAt != entry.JoinedAt ||
                (reason == OnboardingDecisionLabels.AutoPromoteReason &&
                 (current.Application?.ID != entry.Application?.ID ||
                  current.Application?.QuestionnaireSubmittedAt != entry.Application?.QuestionnaireSubmittedAt)))
            {
                break;
            }

            recorded = await current.TryTransitionAsync(db, MemberStatus.FullMember, [current.Status], reason, actorId,
                f => f.Eq(m => m.JoinedAt, entry.JoinedAt));
        }

        if (!recorded)
        {
            throw new InvalidOperationException("Member lifecycle changed during promotion; acceptance was not recorded.");
        }

        logger.LogInformation("Promotion completed for {MemberId}: actor={ActorId} reason={Reason}", entry.ID, actorId, reason);
        try
        {
            await roles.WelcomeAsync(entry, guildConfig, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Promotion succeeded but welcome delivery failed for {MemberId}", entry.ID);
        }
    }
}
